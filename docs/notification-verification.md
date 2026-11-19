# Notification verification - 17 November 2026

## Updated source and isolated application

Build succeeded with zero warnings and zero errors.

Full integration suite: **376 checks passed**.

Headless Microsoft Edge: **172 page/viewport combinations passed** at 360, 390, 768 and 1440 pixels:

- Company Admin: 84 checks.
- Site Engineer: 32 checks.
- Project Manager: 56 checks.

Browser interaction verification includes:

- Clicking an actual rendered notification at mobile and desktop sizes returns HTTP 200.
- Manager and engineer destination URLs explicitly include `/TaskBoard/Index?projectId=...` and select the correct project.
- Previous `/TaskBoard?projectId=...` URLs also open successfully.
- Marking one item read reduces the unread badge.
- Mark-all clears unread rows and the badge; Escape closes the inbox afterwards.
- Disconnecting the browser updates the offline status; returning online reconnects automatically.
- Inbox boundaries remain inside the viewport.

The network-disconnection check exposed delayed offline feedback. The browser now handles explicit offline/online events, closes the old connection and reconnects when online.

Reports: `artifacts/ui/{admin,engineer,manager}/results.json`.
Click-result screenshots: `artifacts/ui/{admin,engineer,manager}/notification-destination-{390,1440}.png`.

## Existing Visual Studio instance checked separately

At verification time, the existing process listening on `https://localhost:7192` was running the Debug Web binary last built at 12:19:18, with process start time 12:19:22. The routing source was changed at 12:27:31.

Anonymous, read-only requests against that instance returned:

- `/TaskBoard?projectId=1`: HTTP 404.
- `/TaskBoard/Index?projectId=1`: HTTP 302 to sign-in (expected without a session).

The tests above run current source in an isolated application using a disposable LocalDB database. They do not establish that the user's existing logged-in Visual Studio session has loaded the new build. Stop the running API and Web applications, rebuild, start them again and sign in to load the routing/link changes in that instance.
