---
type: learnings
title: Sharing a report in Slack — it renders, undocumented
created: 2026-09-29
---

# Sharing a report in Slack — it renders, undocumented

A `report.html` attached to a Slack message was shown as a page in the
channel, not as a download card. The nav rail's scrollspy tracked the scroll,
so the inline script ran. For a team that has nowhere to host reports, or
someone trying proctor who wants to show their team the output, that makes
dropping the file in a channel a real way to share it. It is also a way Slack
never promised, so what we tell users has to be narrower than what we saw.

## What we saw

Observed 2026-09-29 by the maintainer, in the desktop app. Everything worked:
the rail stayed in place (`position: sticky`), in-page anchors jumped to their
sections, `title` tooltips showed on hover, and the scrollspy moved the
highlight. The report is a single self-contained file (inline CSS, one inline
script, the logo as a `data:` URI, no external scripts, stylesheets or fonts),
which the published accounts say is the case that renders reliably.

## What is known

Slack has published nothing about this: no help-center article and no
changelog entry. What follows comes from third-party write-ups, and none of
it is a promise from Slack.

- **Since about mid-June 2026**, a complete standalone `.html` attached to a
  message renders in the channel. HTML pasted into a message body is still
  text. A page that depends on files beside it (CSS, images) renders without
  them. Sources: display.dev/blog/slack-renders-html-attachments (updated
  2026-08-19); stacktr.ee/blog/slack-renders-html-attachments (2026-07-18).
- **The rollout is gradual and uneven, and admins cannot switch it on.** The
  same file renders for one customer and shows a download card for another
  (both blogs, mid-July 2026). A vendor page from September 2026 still calls
  the download card the usual result.
- **Slack Code** (docs.slack.dev/changelog/2026/08/20/slack-code/) lists "HTML
  previews" as something agents can publish, and one hands-on write-up says
  the preview is "limited to static content". That does not fit what we saw.
  It may mean "no backend", or that surface may differ from file attachments.
- **General file limits apply.** Uploads can be up to 1 GB, and Office files
  over 50 MB get no preview (slack.com/help/articles/201330736). No render
  cap for HTML is published. On the free plan, files older than 90 days are
  hidden.
- **Admins can block previews.** Enterprise can block desktop downloads and
  previews per organization, and other plans can do it by IP restriction
  (slack.com/help/articles/360016181794). Slack Connect channels have a list
  of restricted file types, and whether `.html` is on it is unknown.
- **Uploading through the API** now goes through `files.uploadV2`
  (`files.getUploadURLExternal` plus `files.completeUploadExternal`), since
  `files.upload` was retired on 2025-11-12. There is no documented parameter
  that asks for rendering. Whether a report posted from CI renders the same
  way as one dragged in is untested.
- **Security:** Slack has said nothing about how it sandboxes these files.
  A security team may reasonably object to executable HTML from any member
  being rendered in the client, and the Enterprise "block previews" setting
  is what they would reach for.

## What we tell users

> Recent Slack clients may show `report.html` as a page right in the
> channel. The report is self-contained, so nothing is missing when they do.
> Slack is rolling this out unevenly and does not document it, so some
> readers, especially on mobile or in locked-down workspaces, will get a
> download card instead. Downloading the file and opening it in a browser
> always works.

For the renderer, this backs up a rule the report already follows: it must
read completely without JavaScript. The scrollspy is an enhancement. Nothing
in the report may depend on storage, popups, dialogs or external resources,
and a feature like collapsing Tasks should use `<details>`, not a script.

## Open, by hand

Each of these can be settled in a few minutes in a test workspace.

1. **Mobile.** Open the same message on iOS and Android. Does each show a
   page or a card?
2. **Web client.** Open the report at app.slack.com in Chrome. In devtools,
   read the iframe's `sandbox` attribute, its origin and the CSP header.
3. **Capabilities.** Upload a probe page that tries each capability in a
   try/catch and prints the results on the page: `localStorage`,
   `document.cookie`, `fetch`, a CDN `<script>`, a web font, `alert`,
   `navigator.clipboard`, `window.print`, `requestFullscreen`, a nested
   `<iframe>` and `window.open`.
4. **Links.** Does `:target` apply after an anchor jump, and does an external
   link open a browser or navigate the frame?
5. **API upload.** Post the file with `files.uploadV2` using a bot token, as a
   GitHub Action would, and compare the result with a drag-and-drop upload.
6. **Slack Connect and the free plan.** Post the file into a Connect channel,
   and view it in a free workspace.
7. **Size.** Upload padded copies of 5, 20 and 60 MB and see where rendering
   stops.
8. **Admin block.** Turn on "block file downloads and previews" and see what
   readers get.

## Elsewhere, briefly

`actions/upload-artifact` v7 with `archive: false` uploads one file without
zipping it, and the file opens in the browser (github.blog changelog,
2026-02-26). That fits a single `report.html` exactly; whether its scripts
run there is untested. Teams previews only PDF and Office files. Google Drive
shows HTML as text. GitHub Pages and htmlpreview.github.io run scripts but
need a public repository or gist.
