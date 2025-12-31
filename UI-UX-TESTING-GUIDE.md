# UI/UX Testing Guide

## Quick Mobile Navigation Test

### Before You Start:
1. Deploy or run the application locally
2. Open browser developer tools (F12)
3. Toggle device toolbar (Ctrl+Shift+M in Chrome/Edge)

### Test 1: Mobile Navigation Toggle ?
**Priority: CRITICAL**

1. Set viewport to **375px × 667px** (iPhone SE)
2. Navigate to the home page
3. Look for the hamburger menu button (top-right corner)
4. **Click the hamburger button** 
   - ? PASS: Menu slides down/appears below
   - ? FAIL: Nothing happens

5. **Click the hamburger button again**
   - ? PASS: Menu collapses/disappears
   - ? FAIL: Menu stays visible or glitches

6. **Click on a menu item** (e.g., "Home")
   - ? PASS: Menu collapses automatically
   - ?? EXPECTED: This behavior depends on implementation

### Test 2: Scale Verification ??

Compare the new UI with the original by checking these measurements:

#### Desktop (1920px width):
- **Navbar Height:** Should be ~45px (was ~56px)
- **POC Card Width:** Each card should be smaller
- **Base Font Size:** Open inspector, check computed font-size on `<body>` = **14px**
- **Sidebar Width:** Should be ~200px (was 250px)

#### Mobile (375px width):
- **Everything should be readable without zooming**
- **No horizontal scrolling**
- **Touch targets feel comfortable** (not too small)

### Test 3: Responsive Breakpoints ????

Test at these specific widths:

| Width | Expected Layout |
|-------|----------------|
| **1920px** | Desktop - 3 POC cards per row, sidebar visible |
| **1200px** | Desktop - 2 POC cards per row, sidebar visible |
| **768px** | Tablet - 2 POC cards per row, hamburger menu |
| **640px** | Mobile - 1 POC card per row, hamburger menu |
| **375px** | Mobile - 1 POC card per row, compact layout |
| **320px** | Tiny Mobile - Everything still fits and is usable |

## Visual Inspection Checklist ?

### Desktop View (>1200px):
- [ ] Sidebar is visible on the left
- [ ] POC Showcase title is visible in sidebar
- [ ] 3 POC cards displayed per row
- [ ] Cards have proper spacing
- [ ] Text is sharp and readable
- [ ] Footer is at the bottom
- [ ] No scroll on navbar

### Tablet View (768px - 1200px):
- [ ] Hamburger menu appears
- [ ] 2 POC cards per row
- [ ] Sidebar hidden by default
- [ ] Clicking hamburger shows menu overlay
- [ ] Menu items are clearly visible

### Mobile View (<768px):
- [ ] Hamburger menu in top-right
- [ ] 1 POC card per row
- [ ] Cards stack vertically
- [ ] No horizontal scroll
- [ ] Menu overlay covers content when open
- [ ] Easy to tap menu items

### Very Small Mobile (<480px):
- [ ] All content still fits
- [ ] Text remains readable
- [ ] Icons not too small
- [ ] Buttons still tappable

## Feature-Specific Tests ??

### POC Cards:
1. **Hover Effect** (Desktop):
   - Hover over a POC card
   - ? Card should lift up slightly (translateY)
   - ? Shadow should intensify
   - ? Border should appear blue

2. **Click/Tap**:
   - Click/tap a POC card
   - ? Should navigate to the POC page
   - ? No layout shift should occur

3. **Responsive Grid**:
   - Resize browser window slowly
   - ? Cards should reflow smoothly
   - ? No awkward gaps or overlaps

### Navigation Menu:
1. **Active Link Highlight**:
   - Click "Home" in the menu
   - ? "Home" should be highlighted
   - ? Background color should indicate active state

2. **Unlock Features Button**:
   - Click "Unlock Features"
   - ? Modal should appear
   - ? Modal should be centered and sized correctly

### Task Prioritizer (if unlocked):
1. **Task Cards**:
   - View prioritized tasks
   - ? Rank badges (1, 2, 3) should be visible and sized correctly
   - ? Task text should be readable
   - ? Border colors should match rank (red, orange, yellow)

2. **Executive Summary**:
   - View the purple gradient summary box
   - ? Text should be white and readable
   - ? Box should not be too tall

## Browser-Specific Tests ??

### Chrome/Edge:
- [ ] Test on desktop
- [ ] Test in device emulation mode
- [ ] Test with touch simulation enabled

### Firefox:
- [ ] Test on desktop
- [ ] Test in responsive design mode (Ctrl+Shift+M)

### Safari (iOS):
- [ ] Test on actual iPhone if possible
- [ ] Test navigation toggle
- [ ] Check for any iOS-specific issues

### Safari (macOS):
- [ ] Test on desktop
- [ ] Check font rendering

## Performance Checks ?

### Desktop:
- [ ] Page loads quickly
- [ ] Hover effects are smooth
- [ ] No janky animations
- [ ] Scrolling is smooth

### Mobile:
- [ ] Menu toggle is instant
- [ ] No lag when scrolling
- [ ] Touch interactions feel responsive
- [ ] No content flashing or reflows

## Common Issues & Solutions ??

### Issue: Menu doesn't toggle on mobile
**Check:**
- NavMenu.razor has `NavMenuCssClass` returning "show" not null
- CSS has `.nav-scrollable.show { display: block; }`
- JavaScript console for any errors

### Issue: Everything looks too small
**Solution:**
- This is intentional (80% scale)
- Verify base font-size is 14px
- On mobile should be 13px (<768px) or 12px (<480px)

### Issue: Cards overlap or don't fit grid
**Check:**
- Browser width
- CSS grid-template-columns values
- Media queries are loading correctly

### Issue: Text is hard to read
**Consider:**
- Increase contrast if needed
- Check monitor/device brightness
- Base font might need slight increase (14px ? 15px)

## Acceptance Criteria ?

**Must Pass:**
- ? Mobile navigation toggle works
- ? No horizontal scrolling on mobile
- ? All text is readable
- ? Touch targets are adequate (min 44×44px)
- ? Layout doesn't break at any viewport size
- ? Build completes successfully

**Should Pass:**
- ? Hover effects work smoothly
- ? Animations feel polished
- ? Color contrast is good
- ? Footer stays at bottom
- ? No console errors

**Nice to Have:**
- ? Looks professional and modern
- ? Spacing feels balanced
- ? Icons are appropriately sized
- ? Typography hierarchy is clear

## Reporting Issues ??

If you find issues, note:
1. **Browser & Version:** e.g., "Chrome 120 on Windows"
2. **Viewport Size:** e.g., "375×667px"
3. **What Happened:** e.g., "Menu doesn't toggle"
4. **Expected Behavior:** e.g., "Menu should appear when clicking hamburger"
5. **Screenshot or Video:** Always helpful
6. **Console Errors:** Check F12 ? Console tab

## Quick Fixes You Can Try ??

### Text Too Small?
```css
/* In app.css */
html, body {
    font-size: 15px; /* was 14px */
}
```

### Menu Toggle Still Not Working?
```csharp
// In NavMenu.razor, verify this line:
private string? NavMenuCssClass => collapseNavMenu ? "collapse" : "show";
```

### Cards Too Compact?
```css
/* In poc-card.css */
.poc-card {
    padding: 1.5rem; /* increase from 1.2rem */
}
```

## Testing Timeline ??

- **Quick Test:** 5 minutes (mobile toggle + basic responsive)
- **Thorough Test:** 15 minutes (all breakpoints + features)
- **Complete Test:** 30 minutes (all browsers + devices)

## Success! ??

When all tests pass:
- Mobile navigation works perfectly
- UI looks polished and professional
- Everything is 20% more compact
- No regressions in functionality
- Ready for production!
