# ALPHA RELEASE - CURRENTLY ONLY FOR TESTING PURPOSE
![AutoPartyFinder Icon](https://raw.github.com/Paparogue/AutoPartyFinder/99fbcb558c4938d80dd29b177fed287c2aa7ef4a/apf.png)

A Party Finder automation plugin that maintains your recruitment listing and recovers from party changes in FFXIV.

## Installation
Add the following URL to your third-party repository list:
```
https://raw.githubusercontent.com/Paparogue/PaparogueRepo/refs/heads/main/repo.json
```

## Configuration Guide
![AutoPartyFinder Config](https://raw.github.com/Paparogue/AutoPartyFinder/2304853fe6efff64fcaa4b2e02af6691c65ec2d3/config.png)

### Auto-Renewal Feature
- Prevents the 60-minute expiration timeout
- Maintains all recruitment settings and party slots

#### Auto-Renewal Controls
- **Enable/Disable Button**: Toggle automatic renewal on/off
- **Status Indicator**: Shows if auto-renewal is currently active
- **Progress Bar**: Visual countdown until next renewal (0-5 minutes)
- **Timer Display**: Shows minutes elapsed since last recruitment

### Party Size Tracking
- **Current Party Size**: Real-time party member count
- **Last Known Size**: Previous party size for comparison
- **Status Indicator**: Shows if party size is stable or changing
- **Recovery Timer**: 10-second countdown when party size decreases

## How It Works

### Automatic Renewal Process
1. Monitors your active Party Finder listing
2. Tracks time since recruitment started
3. After 55 minutes, automatically:
   - Leaves current duty recruitment
   - Immediately restarts recruitment
   - Preserves all settings and slots 

### Party Recovery Process
1. Detects when party members leave
2. Waits 10 seconds to avoid multiple triggers
3. Executes recovery sequence:
   - Temporarily disables UI interactions
   - Opens recruitment window
   - Restores job restrictions for empty slots
   - Resumes active recruitment

## Important Notes
- **Party Leader Required**: You must be the party leader or solo to use auto-renewal
- **Keep Plugin Active**: Must remain running while recruiting
- **Debug Mode**: Only enable for troubleshooting
