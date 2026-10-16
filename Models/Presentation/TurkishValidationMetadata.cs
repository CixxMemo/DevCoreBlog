using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace DevCoreBlog.Models.Presentation;

/// <summary>Supplies safe Turkish defaults while retaining explicit field-specific validation messages.</summary>
public sealed class TurkishValidationMetadata : IValidationMetadataProvider
{
    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        // MVC otherwise synthesizes an English client Required rule for non-nullable value types.
        if (context.Key.MetadataKind == Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.ModelMetadataKind.Property &&
            context.Key.ContainerType?.Namespace == "DevCoreBlog.Models.Admin" &&
            context.Key.ModelType.IsValueType && Nullable.GetUnderlyingType(context.Key.ModelType) is null &&
            !context.ValidationMetadata.ValidatorMetadata.OfType<RequiredAttribute>().Any())
            context.ValidationMetadata.ValidatorMetadata.Add(new RequiredAttribute { ErrorMessage = "Bu alan gereklidir." });
        foreach (var attribute in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
        {
            if (attribute.ErrorMessage is not null || attribute.ErrorMessageResourceType is not null) continue;
            attribute.ErrorMessage = attribute switch
            {
                RequiredAttribute => "Bu alan gereklidir.",
                StringLengthAttribute { MinimumLength: > 0 } => "Bu alan {2} ile {1} karakter arasında olmalıdır.",
                StringLengthAttribute => "Bu alan en fazla {1} karakter olabilir.",
                RangeAttribute => "Bu alan {1} ile {2} arasında olmalıdır.",
                EmailAddressAttribute => "Geçerli bir e-posta adresi girin.",
                _ => "Bu alanın değeri geçersiz."
            };
        }
    }
}
