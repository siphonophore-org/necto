using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Requests;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The web client check reads the request page with its web client script, and lists only class names the page builds.
/// </summary>
public class WebClientCheckTests
{
    /// <summary>
    /// The page markup holds the request page and its web client script, inside a script tag.
    /// True: the check sees every class the page and its script name.
    /// False: classes the script builds would go unchecked, or the check would read a missing page.
    /// </summary>
    [Fact]
    public void PageMarkup_HoldsThePageAndTheScript()
    {
        var markup = WebClientCheck.PageMarkup();

        Assert.StartsWith(EmbeddedResources.Read("necto_requests_page.html"), markup, StringComparison.Ordinal);
        Assert.Contains("<script>\n" + EmbeddedResources.Read("necto_requests_webclient.js") + "\n</script>", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("The request page is unavailable.", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The web client markup script's classes are read as the page's own.
    /// True: a class the script copies from the web client is checked against it.
    /// False: the check would miss when the web client renames a class the script uses.
    /// </summary>
    [Fact]
    public void PageMarkup_ScriptsClasses_AreReadAsThePages()
    {
        var markup = WebClientCheck.PageMarkup();

        Assert.Contains("cardOverlayButton-br", markup, StringComparison.Ordinal);
        Assert.Contains("cardOverlayButton-br", Siphonophore.Stolon.WebClientCheck.ClassesIn(markup));
    }

    /// <summary>
    /// Every built class name is one the page builds: whole, or its start closing a string the script joins the rest to.
    /// True: the list names only classes the page still builds.
    /// False: the check would look for a class the page no longer uses.
    /// </summary>
    [Fact]
    public void Built_EveryName_IsBuiltByThePage()
    {
        var page = WebClientCheck.PageMarkup();

        Assert.All(WebClientCheck.Built, name => Assert.True(
            page.Contains(name, StringComparison.Ordinal) || Enumerable.Range(6, Math.Max(0, name.Length - 6)).Any(n => page.Contains(name[..n] + "'", StringComparison.Ordinal)),
            name + " is not built by the request page: no start of it is in it"));
    }

    /// <summary>
    /// The classes checked against the web client leave out the ones the page's own stylesheet styles, and keep the rest.
    /// True: the startup check warns only about classes the web client should supply.
    /// False: the page's own classes would be logged as missing from the web client on every start.
    /// </summary>
    [Fact]
    public void ClassesToCheck_LeaveOutThePagesOwnStyles()
    {
        var markup = "<style>.rq-home { color: red; } .appDrawer > .appDrawerItem:hover { }</style><div class=\"rq-home cardBox appDrawer\"><span class=\"appDrawerItem\"></span></div>";

        var classes = Siphonophore.Stolon.WebClientCheck.ClassesToCheck(markup, WebClientCheck.Built);

        Assert.Contains("cardBox", classes);
        Assert.DoesNotContain("rq-home", classes);
        Assert.DoesNotContain("appDrawer", classes);
        Assert.DoesNotContain("appDrawerItem", classes);
    }

    /// <summary>
    /// The real request page's own classes are all styled by its own stylesheet, so none of them is checked.
    /// True: rq- classes never reach the web client check.
    /// False: the check would report them as missing.
    /// </summary>
    [Fact]
    public void ClassesToCheck_RequestPage_LeaveOutItsOwnClasses()
    {
        var classes = Siphonophore.Stolon.WebClientCheck.ClassesToCheck(WebClientCheck.PageMarkup(), WebClientCheck.Built);

        Assert.DoesNotContain(classes, c => c.StartsWith("rq-", StringComparison.Ordinal));
        Assert.DoesNotContain("appDrawer", classes);
    }
}
