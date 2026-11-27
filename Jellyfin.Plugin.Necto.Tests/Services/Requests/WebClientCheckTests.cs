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
    /// The page markup is the request page with its script, whose classes are read as the page's.
    /// True: the check sees every class the page and its script name.
    /// False: classes the page uses would go unchecked, or the check would read a missing page.
    /// </summary>
    [Fact]
    public void PageMarkup_IsTheRequestPage()
    {
        var markup = WebClientCheck.PageMarkup();

        Assert.Equal(EmbeddedResources.Read("necto_requests_page.html"), markup);
        Assert.Contains("skinHeader", Siphonophore.Stolon.WebClientCheck.ClassesIn(markup));
        Assert.DoesNotContain("The request page is unavailable.", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every built class name is a card shape the page passes to Stolon's builders, quoted in its script.
    /// True: the list names only shapes the page still builds.
    /// False: the check would look for a class the page no longer uses.
    /// </summary>
    [Fact]
    public void Built_EveryName_IsAShapeThePageBuilds()
    {
        var page = WebClientCheck.PageMarkup();

        Assert.All(WebClientCheck.Built, name =>
        {
            var shape = name.StartsWith("cardPadder-", StringComparison.Ordinal) ? name["cardPadder-".Length..] : name[..^"Card".Length];
            Assert.True(page.Contains("'" + shape + "'", StringComparison.Ordinal), name + " is not built by the request page");
        });
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
