using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PulsePoint.Pages;

public class ManageSetupModel : PageModel
{
    private readonly AppState _state;
    public string Error { get; private set; } = "";

    public ManageSetupModel(AppState state) => _state = state;

    public IActionResult OnGet()
    {
        if (_state.HasPassword()) return Redirect("/manage");
        return Page();
    }

    public IActionResult OnPost(string password, string confirm)
    {
        if (_state.HasPassword()) return Redirect("/manage");

        if (string.IsNullOrWhiteSpace(password))
        { Error = "Password cannot be empty."; return Page(); }

        if (password != confirm)
        { Error = "Passwords do not match."; return Page(); }

        if (password.Length < 6)
        { Error = "Password must be at least 6 characters."; return Page(); }

        _state.SetPassword(password);
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
