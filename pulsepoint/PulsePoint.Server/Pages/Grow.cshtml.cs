using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PulsePoint.Pages;

public class GrowModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly AppState _state;

    [FromQuery(Name = "key")]
    public string? Key { get; set; }

    public string RtspUrl { get; private set; } = "";
    public string HlsUrl  { get; private set; } = "";

    public GrowModel(IConfiguration config, AppState state)
    {
        _config = config;
        _state  = state;
    }

    public IActionResult OnGet()
    {
        var expected = _config["ApiKey"] ?? "";
        if (!string.IsNullOrEmpty(expected) && Key != expected)
            return Redirect("/");
        ViewData["ApiKey"] = Key ?? "";
        ViewData["Page"]   = "grow";
        RtspUrl = _state.Db.GetSetting("grow_rtsp_url") ?? "";
        HlsUrl  = _state.Db.GetSetting("grow_hls_url")  ?? "";
        return Page();
    }
}
