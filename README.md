# AutoPartyFinder
![AutoPartyFinder Icon](https://raw.github.com/Paparogue/AutoPartyFinder/99fbcb558c4938d80dd29b177fed287c2aa7ef4a/apf.png)

Automates Party Finder recruitment in FFXIV by preventing listing expiration and recovering from party changes.

## ⚠️ ALPHA VERSION - TESTING PHASE
This plugin is currently in early development. Expect bugs, crashes, and unexpected behavior. Use at your own risk and please report any issues.

## What It Does

- **Prevents 60-minute timeout** - Automatically renews your Party Finder listing every 55 minutes
- **Recovers from leavers** - Restores job restrictions when party members leave
- **Customizable job slots** - Override which jobs can join each party slot

## Installation

Add this URL to your third-party repository list:
```
https://raw.githubusercontent.com/Paparogue/PaparogueRepo/refs/heads/main/repo.json
```

## How to Use

1. **Open AutoPartyFinder**
   - Type `/apf` in chat to open the plugin window
   - Go to the "Auto-Renewal" tab

2. **Enable Auto-Renewal**
   - Click "ENABLE AUTO-RENEWAL" button
   - The plugin now monitors your listing and prevents the 60-minute timeout
  
3. **Set up your Party Finder listing as normal**
   - Open Party Finder and configure your recruitment (duty, description, job requirements)
   - Click "Recruit Members" to start - this saves your settings automatically

4. **That's it!** The plugin runs in the background:
   - Automatically renews your listing every 55 minutes
   - Restores job slots when party members leave
   - You can close the Party Finder window - recruitment continues
   - Check the "Status" tab anytime to see party information
     

![AutoPartyFinder Config1](https://raw.github.com/Paparogue/AutoPartyFinder/595d3141615b94c9a0f0a370365dd85fa689af28/Images/UI_1.png)

![AutoPartyFinder Config2](https://raw.github.com/Paparogue/AutoPartyFinder/595d3141615b94c9a0f0a370365dd85fa689af28/Images/UI_2.png)

![AutoPartyFinder Config3](https://raw.github.com/Paparogue/AutoPartyFinder/595d3141615b94c9a0f0a370365dd85fa689af28/Images/UI_3.png)

![AutoPartyFinder Config4](https://raw.github.com/Paparogue/AutoPartyFinder/595d3141615b94c9a0f0a370365dd85fa689af28/Images/UI_4.png)

## Features

### Auto-Renewal
Keeps your Party Finder listing active indefinitely by refreshing it every 55 minutes, just before the 60-minute expiration.

### Party Recovery
When someone leaves your party:
- Waits 10 seconds (to avoid multiple triggers)
- Automatically restores the job restrictions for empty slots
- Continues recruiting without manual intervention

### Job Override
Change job requirements without restarting recruitment:
- When you start recruiting, the plugin saves your original job settings
- Use Job Override to modify requirements mid-recruitment (e.g., need a different healer type)
- **Important**: This overrides your saved settings - disable it to return to original requirements
- Enable in the "Job Override" tab and configure per slot
- Supports role groups (All Tanks, Pure Healers, Barrier Healers, etc.)

## Requirements

- You must be the **party leader** or **solo**
- Keep the plugin running while recruiting
- Party Finder window can be closed - the plugin works in the background
