"""Real rendered pages against the integration harness, never production test headers."""
import argparse
import json
import os
from pathlib import Path
import sys
from urllib.parse import urlparse

sys.path.insert(0, str(Path('.tools/browser').resolve()))
from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser()
parser.add_argument('--url', required=True)
parser.add_argument('--role', choices=['admin', 'manager', 'engineer'], required=True)
parser.add_argument('--project', required=True)
parser.add_argument('--invoice-id')
parser.add_argument('--invoice-link')
args = parser.parse_args()
if urlparse(args.url).hostname != '127.0.0.1':
    raise SystemExit('Browser fixtures may only connect to the local test harness.')
headers = {'X-Workspace-Test': '1'} if args.role == 'admin' else {'X-Test-Role': args.role}
if args.role == 'admin':
    routes = ['/Dashboard/Index', '/Projects/Index', '/Projects/Create', f'/Projects/Edit/{args.project}', '/TeamMembers/Index', '/TeamMembers/Create', '/CompanyProfile/Index', '/ProjectAssignments/Index']
    routes += ['/Account/Login', '/Account/Register', '/PasswordRecovery/Forgot']
    routes += ['/Workspace/' + name for name in ['Schedule', 'Documents', 'Reports', 'Activity', 'Settings']]
    routes += ['/Billing/Index', '/Billing/Create']
    if args.invoice_id: routes += ['/Billing/Details/' + args.invoice_id]
    if args.invoice_link: routes += ['/Billing/Pay?link=' + args.invoice_link]
else:
    routes = ['/TaskBoard?tab=' + tab for tab in ['tasks', 'daily', 'notifications', 'reports', 'history']]
    if args.role == 'manager':
        routes += ['/ManagerDashboard/Index', '/ManagerProjects/Index', f'/ManagerProjects/Details/{args.project}']
        routes += ['/ManagerWorkspace/' + name for name in ['Schedule', 'Documents', 'Reports', 'Activity', 'Settings']]
    else:
        routes += ['/EngineerDashboard/Index', '/EngineerWorkspace/Settings']
out = Path('artifacts/ui') / args.role
out.mkdir(parents=True, exist_ok=True)
failures = []
notification_checks = []
routes += ['/Subscription/Index']
if os.environ.get('UI_ROUTES'):
    routes = [route for route in routes if route in os.environ['UI_ROUTES'].split(',')]
with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', headless=True)
    context = browser.new_context(extra_http_headers=headers, reduced_motion='reduce')
    # Local system-font fallback is deliberate: captures do not depend on a CDN.
    context.route('**/*', lambda route: route.continue_() if route.request.url.startswith(args.url) or route.request.url.startswith('data:') else route.abort())
    page = context.new_page()
    for width in [360, 390, 768, 1440]:
        page.set_viewport_size({'width': width, 'height': 900 if width < 1000 else 1000})
        for number, route in enumerate(routes):
            response = page.goto(args.url + route, wait_until='load')
            if not response or response.status >= 400:
                failures.append({'width': width, 'route': route, 'error': 'HTTP ' + str(response.status if response else 'none')})
                continue
            page.wait_for_timeout(80)
            overflow = page.evaluate('''() => {
                const width = document.documentElement.clientWidth;
                return document.documentElement.scrollWidth > width + 2
                    ? [...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > width + 2 && getComputedStyle(e).position !== 'fixed').slice(0,8).map(e => e.tagName + '.' + e.className)
                    : [];
            }''')
            if overflow: failures.append({'width': width, 'route': route, 'overflow': overflow})
            if width in [390, 1440]: page.screenshot(path=str(out / f'{number:02d}-{width}.png'), full_page=True)
            bell = page.locator('#liveNotifications')
            if number == 0 and bell.count():
                page.wait_for_function("document.getElementById('liveNoticeConnection').textContent === 'Live updates connected'", timeout=15000)
                bell.locator('summary').click()
                panel = page.locator('#liveNoticePanel')
                box = panel.bounding_box()
                if not box or box['x'] < 0 or box['x'] + box['width'] > width + 2 or box['y'] < 0:
                    failures.append({'width': width, 'route': route, 'error': 'Notification panel exceeds viewport'})
                page.screenshot(path=str(out / f'notifications-{width}.png'), full_page=False)
                if width == 360 and page.locator('#liveNoticeItems article.unread button').count():
                    unread_before = int(page.locator('#liveNoticeCount').inner_text())
                    page.locator('#liveNoticeItems article.unread button').first.click()
                    page.wait_for_function("before => Number(document.getElementById('liveNoticeCount').textContent) === before - 1", arg=unread_before, timeout=10000)
                    notification_checks.append('Individual read updates unread badge')
                if width == 1440 and page.locator('#liveNoticeReadAll').is_enabled():
                    page.locator('#liveNoticeReadAll').click()
                    page.wait_for_function("document.getElementById('liveNoticeCount').hidden", timeout=10000)
                    if page.locator('#liveNoticeItems article.unread').count():
                        failures.append({'route': route, 'error': 'Mark all read left unread inbox rows'})
                bell.press('Escape')
                if bell.get_attribute('open') is not None:
                    failures.append({'width': width, 'route': route, 'error': 'Escape did not close notifications'})
            if number == 0 and width in [390, 1440] and bell.count():
                bell.locator('summary').click()
                link = page.locator('#liveNoticeItems article > a').first
                if link.count():
                    destination = link.get_attribute('href')
                    if args.role != 'admin' and destination != f'/TaskBoard/Index?projectId={args.project}':
                        failures.append({'route': route, 'error': 'Notification does not target explicit Index action', 'destination': destination})
                    with page.expect_navigation(wait_until='load') as navigation:
                        link.click()
                    result = navigation.value
                    if not result or result.status != 200 or 'Page not found' in page.title():
                        failures.append({'route': route, 'error': 'Clicking notification failed', 'destination': destination, 'status': result.status if result else None})
                    elif args.role != 'admin' and page.locator('select[name="projectId"]').input_value() != args.project:
                        failures.append({'route': route, 'error': 'Notification opened wrong project'})
                    else:
                        notification_checks.append(f'Actual notification click opens correct project at {width}px (HTTP 200)')
                    page.screenshot(path=str(out / f'notification-destination-{width}.png'), full_page=True)
                    if args.role != 'admin':
                        legacy = page.goto(args.url + f'/TaskBoard?projectId={args.project}', wait_until='load')
                        if not legacy or legacy.status != 200:
                            failures.append({'route': route, 'error': 'Old notification URL still fails'})
                        else:
                            notification_checks.append(f'Old notification URL works at {width}px')
                    page.goto(args.url + route, wait_until='load')
                elif args.role == 'admin' and page.locator('#liveNoticeCount').is_hidden():
                    notification_checks.append('Admin inbox stays empty for own actions')
                    bell.press('Escape')
                else:
                    failures.append({'route': route, 'error': 'No seeded notification available for click verification'})
            if width == 390 and route == '/Dashboard/Index':
                video = page.locator('.clarity-feature-video')
                if video.count():
                    if video.evaluate('(v) => v.currentSrc !== "" || !v.paused'):
                        failures.append({'route': route, 'error': 'Reduced motion loaded or played video automatically'})
                    page.get_by_role('button', name='Play background video').click()
                    page.wait_for_function('document.querySelector("video").currentTime > 0.1')
                    page.get_by_role('button', name='Pause background video').click()
                    if not video.evaluate('(v) => v.paused && v.muted && v.videoWidth > 0'):
                        failures.append({'route': route, 'error': 'Video playback, decoding or pause failed'})
                page.locator('#projectSearch').fill('no-such-project-clarity-check')
                if not page.locator('#searchEmptyRow').is_visible():
                    failures.append({'route': route, 'error': 'Project search did not show its empty state'})
                page.locator('#resetFilters').click()
                if page.locator('.project-data-row:visible').count() == 0:
                    failures.append({'route': route, 'error': 'Reset did not restore project rows'})
                page.locator('#accountMenu summary').click()
                page.keyboard.press('Escape')
                if page.locator('#accountMenu').get_attribute('open') is not None:
                    failures.append({'route': route, 'error': 'Account menu did not close with Escape'})
            if width == 390 and route == '/Projects/Index':
                page.locator('#portfolioSearch').fill('no-such-project-check')
                if not page.locator('#portfolioEmpty').is_visible():
                    failures.append({'route': route, 'error': 'Project portfolio search empty state failed'})
                page.locator('#portfolioReset').click()
                page.locator('[data-view="list"]').click()
                if not page.locator('#portfolioCards').evaluate('(e) => e.classList.contains("is-list")'):
                    failures.append({'route': route, 'error': 'Project list toggle failed'})
                page.locator('[data-view="grid"]').click()
                page.locator('.project-details-toggle').first.click()
                if page.locator('.portfolio-details:visible').count() != 1:
                    failures.append({'route': route, 'error': 'Project overview disclosure failed'})
                page.locator('[data-status-tab="Planning"]').click()
                if page.locator('.portfolio-card:visible').count() != page.locator('[data-deadline]:visible').count():
                    failures.append({'route': route, 'error': 'Project cards and deadlines filters differ'})
            if width == 390 and route == '/Billing/Create':
                page.locator('#add-invoice-line').click()
                if page.locator('.invoice-line').count() != 2:
                    failures.append({'route': route, 'error': 'Add invoice item failed'})
                page.locator('.remove-line').first.click()
                if page.locator('[name="Input.Lines[0].Description"]').count() != 1:
                    failures.append({'route': route, 'error': 'Invoice items did not renumber after removal'})
            if width < 650 and page.locator('.invoice-items tfoot td').count():
                total = page.locator('.invoice-items tfoot td').bounding_box()
                if total and total['x'] + total['width'] > width + 2:
                    failures.append({'route': route, 'error': 'Invoice total is clipped on mobile'})
            if width == 1440 and route.startswith('/Billing/Pay?'):
                # Inspect the real form/CSRF response but stop its redirect before any external request.
                # Playwright routing is not re-applied to later URLs in a redirected request chain.
                checkout_redirect = []
                def intercept_checkout(intercepted):
                    result = intercepted.fetch(max_redirects=0)
                    checkout_redirect.append((result.status, result.headers.get('location', '')))
                    intercepted.fulfill(status=200, content_type='text/html', body='<h1>Test hosted checkout</h1>')
                page.route('**/Billing/Begin', intercept_checkout)
                page.get_by_role('button', name='Pay with Stripe').click()
                page.get_by_role('heading', name='Test hosted checkout').wait_for(state='visible', timeout=10000)
                if checkout_redirect != [(302, 'https://checkout.stripe.com/test')]:
                    failures.append({'route': route, 'error': 'Hosted checkout redirect failed'})
            if width == 390 and number == 0:
                toggle = page.locator('.workspace-menu-button')
                if toggle.count() and toggle.is_visible():
                    if page.locator('aside nav').first.is_visible(): failures.append({'route': route, 'error': 'Collapsed mobile navigation is still visible'})
                    toggle.click()
                    if toggle.get_attribute('aria-expanded') != 'true': failures.append({'route': route, 'error': 'Mobile navigation did not expand'})
                    page.screenshot(path=str(out / 'navigation-390.png'), full_page=False)
                    toggle.click()
    page.goto(args.url + routes[0], wait_until='load')
    if page.locator('#liveNotifications').count():
        page.wait_for_function("document.getElementById('liveNoticeConnection').textContent === 'Live updates connected'", timeout=15000)
        context.set_offline(True)
        try:
            page.wait_for_function("/Offline|Reconnecting/.test(document.getElementById('liveNoticeConnection').textContent)", timeout=15000)
        except Exception:
            failures.append({'error': 'Inbox did not report connection loss'})
        finally:
            context.set_offline(False)
        try:
            page.wait_for_function("document.getElementById('liveNoticeConnection').textContent === 'Live updates connected'", timeout=15000)
            notification_checks.append('Inbox reconnects after browser goes offline and returns online')
        except Exception:
            failures.append({'error': 'Inbox did not reconnect after network recovery'})
    browser.close()
(out / 'results.json').write_text(json.dumps({'pages': len(routes), 'widths': [360,390,768,1440], 'failures': failures, 'notification_checks': notification_checks}, indent=2))
print(f'BROWSER {args.role}: {len(routes) * 4} page/viewport checks; {len(failures)} failures', flush=True)
for failure in failures: print(json.dumps(failure), flush=True)
raise SystemExit(1 if failures else 0)
