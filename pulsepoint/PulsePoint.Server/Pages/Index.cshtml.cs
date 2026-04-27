using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PulsePoint.Pages;

public class IndexModel : PageModel
{
    private readonly IConfiguration _config;
    public string Key { get; private set; } = "";

    public IndexModel(IConfiguration config) => _config = config;

    public IActionResult OnGet([FromQuery] string? key)
    {
        var required = _config["ApiKey"];
        if (!string.IsNullOrEmpty(required) && key != required)
            return Redirect("/?key=");
        Key = key ?? "";
        ViewData["ApiKey"] = Key;
        ViewData["Page"] = "dashboard";
        return Page();
    }
}
