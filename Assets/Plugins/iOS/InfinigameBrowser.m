// Opens a web page over the game without leaving it, and puts it away again. Signing in to
// ChatGPT sends the browser back to a page the game itself serves, so the game has to stay
// awake behind the page: leaving for Safari would put it to sleep.
#import <SafariServices/SafariServices.h>

extern UIViewController *UnityGetGLViewController(void);

static SFSafariViewController *page;

void InfinigameBrowse(const char *url)
{
    page = [[SFSafariViewController alloc] initWithURL:[NSURL URLWithString:[NSString stringWithUTF8String:url]]];
    [UnityGetGLViewController() presentViewController:page animated:YES completion:nil];
}

void InfinigameBrowseDone(void)
{
    [page dismissViewControllerAnimated:YES completion:nil];
    page = nil;
}
