using System.ComponentModel;

namespace Apex.AgenticEntityExtractor.Models;

public class ExtractionRequest
{
    [DefaultValue("")]
    public string? InputText { get; set; }
    
    public IFormFile? InputImage { get; set; }
}
