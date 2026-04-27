using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PulsePoint.Pages;

public class ManageLoginModel : PageModel
{
    private readonly AppState _state;
    public string Error { get; private set; } = "";

    public ManageLoginModel(AppState state) => _state = state;

    public IActionResult OnGet()
    {
        if (!_state.HasPassword()) return Redirect("/managesetup");
        if (_state.ValidateSession(HttpContext)) return Redirect("/manage");
        return Page();
    }

    public IActionResult OnPost(string password)
    {
        if (!_state.HasPassword()) return Redirect("/managesetup");

        if (!_state.VerifyPassword(password))
        {
            Error = "Incorrect password.";
            return Page();
        }

        var token = _state.CreateSession();
        Response.Cookies.Append(AppState.SessionCookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(8)
        });
        return Redirect("/manage");
    }
}
