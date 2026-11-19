using DiceModders.UiKit.Custom;
using UnityEngine;

namespace DiceModders.IslandTools;

/// <summary>The paged tutorial window ("Help" in the editor's title bar, or Settings > Game > Island Tools). Pages: texts.json help.t1..t8 / help.p1..p8.</summary>
internal static class HelpWindow
{
    private const int PageCount = 8;
    private static UiWindow _win;
    private static UiText _title, _body;
    private static UiScroll _scroll;
    private static int _page;

    private static string Title(int page) => (page + 1) + "/" + PageCount + "  " + Tx.T("help.t" + (page + 1));
    private static string Page(int page) => Tx.T("help.p" + (page + 1));

    internal static bool IsOpen => _win != null;

    internal static void Toggle() { if (IsOpen) Close(); else Open(); }

    internal static void Close() => _win?.Close();

    internal static void Open()
    {
        if (IsOpen) { _win.BringToFront(); return; }
        _page = 0;
        _win = Windows.Ui?.Custom.Window(w =>
        {
            w.Title(Tx.T("help.title")).Size(760f, 600f).Position(140f, 90f).Draggable().Closable().Fullscreenable().FullscreenButton();
            w.OnClose(() => { _win = null; _title = _body = null; _scroll = null; });
            _title = w.Body.Text(Title(0))?.Size(22f).Color(new Color(1f, 0.82f, 0.45f, 1f));
            _scroll = w.Body.Scroll(s => _body = s.Paragraph(Page(0))?.Size(18f))?.FillWidth().FillHeight() as UiScroll;
            w.Body.Row(r =>
            {
                r.Button(Tx.T("help.previous"), () => Go(-1))?.FillWidth();
                r.Button(Tx.T("help.openEditor"), EditorWindow.Open)?.FillWidth();
                r.Button(Tx.T("help.next"), () => Go(1))?.FillWidth();
            })?.Height(36f);
        });
    }

    private static void Go(int d)
    {
        _page = Mathf.Clamp(_page + d, 0, PageCount - 1);
        if (_title != null) _title.Value = Title(_page);
        if (_body != null) _body.Value = Page(_page);
        _scroll?.ToTop();
    }
}
