using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PulsePoint.Pages;

public class ManageModel : PageModel
{
    private readonly AppState _state;
    private readonly IConfiguration _config;

    public ManageModel(AppState state, IConfiguration config)
    {
        _state = state;
        _config = config;
    }

    public IActionResult OnGet()
    {
        if (!_state.HasPassword()) return Redirect("/managesetup");
        if (!_state.ValidateSession(HttpContext)) return Redirect("/managelogin");
        ViewData["ApiKey"] = _config["ApiKey"] ?? "";
        ViewData["Page"] = "manage";
        return Page();
    }
}
