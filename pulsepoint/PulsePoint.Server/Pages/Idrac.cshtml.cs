using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PulsePoint.Pages;

public class IdracModel : PageModel
{
    private readonly AppState _state;
    private readonly IConfiguration _config;

    [FromQuery(Name = "key")]
    public string? Key { get; set; }

    public IdracModel(AppState state, IConfiguration config)
    {
        _state  = state;
        _config = config;
    }

    public IActionResult OnGet()
    {
        var expected = _config["ApiKey"] ?? "";
        if (!string.IsNullOrEmpty(expected) && Key != expected)
            return Redirect("/");
        ViewData["ApiKey"] = Key ?? "";
        ViewData["Page"]   = "idrac";
        return Page();
    }
}
