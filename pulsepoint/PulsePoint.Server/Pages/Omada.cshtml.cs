using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PulsePoint.Pages;

public class OmadaModel : PageModel
{
    private readonly IConfiguration _config;

    [FromQuery(Name = "key")]
    public string? Key { get; set; }

    public OmadaModel(IConfiguration config) => _config = config;

    public IActionResult OnGet()
    {
        var expected = _config["ApiKey"] ?? "";
        if (!string.IsNullOrEmpty(expected) && Key != expected)
            return Redirect("/");
        ViewData["ApiKey"] = Key ?? "";
        ViewData["Page"]   = "omada";
        return Page();
    }
}
