using DevCoreBlog.Core.Documents;
using DevCoreBlog.Models.Admin;
using DevCoreBlog.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevCoreBlog.Controllers;

/// <summary>Preparing and sending tests are separate authenticated POST actions; HTTP never selects a recipient or starts a campaign.</summary>
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminTestEmailController(TestEmailMessageFactory factory, TestEmailSubmissionService service, TestEmailOptions options) : Controller
{
    private const int FormRequestBytes = DocumentLimits.Utf8Bytes * 3 + 65_536;
    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(FormRequestBytes)]
    [RequestFormLimits(ValueLengthLimit = FormRequestBytes, ValueCountLimit = 6, KeyLengthLimit = 64)]
    public IActionResult Prepare([Bind(Prefix = "Input")] TestEmailInput input)
    {
        var validation = ModelState.IsValid ? factory.Compose(input.DocumentJson, input.Subject) : new(TestEmailCompositionStatus.Invalid, null);
        if (validation.Status is TestEmailCompositionStatus.Invalid or TestEmailCompositionStatus.TooLarge)
        {
            Response.StatusCode = validation.Status == TestEmailCompositionStatus.TooLarge ? 413 : 400;
            return View("Invalid");
        }
        input.RequestId = Guid.NewGuid();
        ModelState.Remove("Input.RequestId");
        return View("Compose", new TestEmailComposeModel(input, service.IsReady, options.Recipient?.Address));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("TestEmailLimiter")]
    [RequestSizeLimit(FormRequestBytes)]
    [RequestFormLimits(ValueLengthLimit = FormRequestBytes, ValueCountLimit = 6, KeyLengthLimit = 64)]
    public async Task<IActionResult> Send([Bind(Prefix = "Input")] TestEmailInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            return View("Compose", new TestEmailComposeModel(input, service.IsReady, options.Recipient?.Address));
        }
        var result = await service.SubmitAsync(input.RequestId, input.DocumentJson, input.Subject, cancellationToken);
        Response.StatusCode = result.Status switch
        {
            TestEmailResultStatus.Accepted => 200,
            TestEmailResultStatus.InProgress => 202,
            TestEmailResultStatus.Invalid => 400,
            TestEmailResultStatus.TooLarge => 413,
            TestEmailResultStatus.Conflict or TestEmailResultStatus.Suppressed => 409,
            TestEmailResultStatus.Limited => 429,
            TestEmailResultStatus.Rejected => 502,
            _ => 503
        };
        if (result.RetryAfter is { } delay)
            Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return View("Result", new TestEmailResultModel(result));
    }
}
