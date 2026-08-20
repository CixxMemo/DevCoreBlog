using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DevCoreBlog.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // İsteği bir sonraki middleware'e ilet
            await _next(context);
        }
        catch (Exception ex)
        {
            // Hata fırlatıldığında buraya düşer, loglama işlemi yapılır
            _logger.LogError(ex, "Sistemde beklenmeyen bir hata oluştu! İstek Yolu: {Path}", context.Request.Path);

            // Kullanıcıyı hata sayfasına yönlendir (MVC uygulaması olduğu için redirect kullanıyoruz)
            context.Response.Redirect("/Home/Error");
        }
    }
}
