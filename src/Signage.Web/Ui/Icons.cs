using Microsoft.AspNetCore.Html;

namespace Signage.Web.Ui;

/// <summary>Inline stroke icons. Markup only, so they are allowed by the page CSP.</summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["grid"] = """<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>""",
        ["monitor"] = """<rect x="2.5" y="4" width="19" height="13" rx="2"/><path d="M8 21h8M12 17v4"/>""",
        ["link"] = """<path d="M10 13a5 5 0 0 0 7.07 0l3-3a5 5 0 0 0-7.07-7.07l-1.5 1.5"/><path d="M14 11a5 5 0 0 0-7.07 0l-3 3a5 5 0 0 0 7.07 7.07l1.5-1.5"/>""",
        ["layers"] = """<path d="M12 3 2.5 8 12 13l9.5-5z"/><path d="m2.5 12.5 9.5 5 9.5-5"/><path d="m2.5 17 9.5 5 9.5-5"/>""",
        ["upload"] = """<path d="M12 15V3"/><path d="m7 8 5-5 5 5"/><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>""",
        ["slides"] = """<path d="M2 4h20"/><rect x="4" y="4" width="16" height="11" rx="1"/><path d="M12 15v3M8 21l4-3 4 3"/>""",
        ["calendar"] = """<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M16 3v4M8 3v4M3 10h18"/>""",
        ["users"] = """<circle cx="9" cy="8" r="4"/><path d="M2 21a7 7 0 0 1 14 0"/><path d="M16 3.5a4 4 0 0 1 0 9M22 21a7 7 0 0 0-4-6.3"/>""",
        ["list"] = """<path d="M9 6h12M9 12h12M9 18h12"/><path d="M4 6h.01M4 12h.01M4 18h.01"/>""",
        ["server"] = """<rect x="3" y="3" width="18" height="8" rx="2"/><rect x="3" y="13" width="18" height="8" rx="2"/><path d="M7 7h.01M7 17h.01"/>""",
        ["database"] = """<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.66 3.58 3 8 3s8-1.34 8-3V5"/><path d="M4 12c0 1.66 3.58 3 8 3s8-1.34 8-3"/>""",
        ["drive"] = """<path d="M22 12H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/><path d="M6 16h.01M10 16h.01"/>""",
        ["cpu"] = """<rect x="5" y="5" width="14" height="14" rx="2"/><rect x="9" y="9" width="6" height="6" rx="1"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M19 9h3M19 15h3M2 9h3M2 15h3"/>""",
        ["clock"] = """<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>""",
        ["logout"] = """<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="m16 17 5-5-5-5M21 12H9"/>""",
        ["check"] = """<path d="m5 12.5 4.5 4.5L19 7.5"/>""",
        ["alert"] = """<path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4M12 17h.01"/>""",
        ["x"] = """<path d="M18 6 6 18M6 6l12 12"/>""",
        ["plus"] = """<path d="M12 5v14M5 12h14"/>""",
        ["play"] = """<path d="M7 4.5v15l12-7.5z"/>""",
        ["pause"] = """<path d="M8 5v14M16 5v14"/>""",
        ["chevron-left"] = """<path d="m15 18-6-6 6-6"/>""",
        ["chevron-right"] = """<path d="m9 18 6-6-6-6"/>""",
        ["arrow-left"] = """<path d="M19 12H5M12 19l-7-7 7-7"/>""",
        ["file"] = """<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/>""",
        ["eye"] = """<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>""",
        ["refresh"] = """<path d="M21 12a9 9 0 1 1-2.64-6.36L21 8"/><path d="M21 3v5h-5"/>""",
        ["send"] = """<path d="m22 2-7 20-4-9-9-4z"/><path d="M22 2 11 13"/>""",
        ["archive"] = """<rect x="2" y="3" width="20" height="5" rx="1"/><path d="M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8M10 12h4"/>""",
        ["folder"] = """<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>""",
        ["folder-plus"] = """<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><path d="M12 10.5v5M9.5 13h5"/>""",
        ["inbox"] = """<path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>""",
        ["trash"] = """<path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/>""",
        ["undo"] = """<path d="M3 7v6h6"/><path d="M21 17a9 9 0 0 0-15-6.7L3 13"/>""",
        ["ban"] = """<circle cx="12" cy="12" r="9"/><path d="m5.7 5.7 12.6 12.6"/>""",
        ["more"] = """<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>""",
        ["pencil"] = """<path d="M17 3a2.85 2.85 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5z"/>""",
        ["search"] = """<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/>""",
        ["menu"] = """<path d="M4 6h16M4 12h16M4 18h16"/>""",
        ["info"] = """<circle cx="12" cy="12" r="9"/><path d="M12 16v-4M12 8h.01"/>""",
        ["video"] = """<rect x="2" y="6" width="14" height="12" rx="2"/><path d="m16 10 6-3v10l-6-3z"/>""",
        ["user"] = """<circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/>""",
        ["expand"] = """<path d="M8 3H5a2 2 0 0 0-2 2v3M21 8V5a2 2 0 0 0-2-2h-3M3 16v3a2 2 0 0 0 2 2h3M16 21h3a2 2 0 0 0 2-2v-3"/>""",
        ["globe"] = """<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/>""",
        ["sun"] = """<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41"/>""",
        ["moon"] = """<path d="M20.5 14.5A8.5 8.5 0 1 1 9.5 3.5a7 7 0 0 0 11 11z"/>""",
        ["pin"] = """<path d="M9 3h6"/><path d="M10 3v5.5L7 12v2h10v-2l-3-3.5V3"/><path d="M12 14v7"/>""",
        ["chevrons-up-down"] = """<path d="m7 15 5 5 5-5"/><path d="m7 9 5-5 5 5"/>"""
    };

    public static IHtmlContent Get(string name, string? cssClass = null) =>
        new HtmlString($"""<svg class="icon{(cssClass is null ? "" : " " + cssClass)}" viewBox="0 0 24 24" aria-hidden="true" focusable="false">{Paths[name]}</svg>""");
}
