using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PulsePoint.Pages;

public class ManageLogoutModel : PageModel
{
    private readonly AppState _state;

    public ManageLogoutModel(AppState state) => _state = state;

    public IActionResult OnGet()
    {
        _state.RevokeSession(HttpContext);
        Response.Cookies.Delete(AppState.SessionCookie);
        return Redirect("/managelogin");
    }
}
