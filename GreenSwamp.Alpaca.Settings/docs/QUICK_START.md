# Quick Start Guide - Settings Profiles

## 5-Minute Setup

### Step 1: Access Profiles (30 seconds)
1. Start GreenSwamp Alpaca Server
2. Open browser to `http://localhost:31426`
3. Click **Settings Profiles** in menu

### Step 2: View Default Profiles (30 seconds)
You'll see 3 default profiles:
- **German Equatorial (Default)** ? Most common
- **Fork Equatorial (Default)**
- **Alt-Azimuth (Default)**

### Step 3: Create Your Profile (2 minutes)
1. Click **"New Profile"**
2. Name: `my-mount` (or your mount name)
3. Select alignment mode for your mount:
   - German Equatorial (GEM) = Most equatorial mounts
   - Fork Equatorial = Fork mounts
   - Alt-Azimuth = Dobsonians, some computerized mounts
4. Copy from: Select a default profile
5. Click **"Create"**

### Step 4: Configure Your Mount (2 minutes)
1. Click **"Edit"** on your new profile
2. **Connection Tab**:
   - Set Port (COM3, COM5, etc.)
   - Set Baud Rate (usually 9600 or 115200)
3. **Location Tab**:
   - Set your Latitude (N is +, S is -)
   - Set your Longitude (E is +, W is -)
   - Set Elevation (meters)
4. Click **"Save Changes"**

### Step 5: Activate & Use (30 seconds)
1. Click **"Activate"** on your profile
2. **Restart the application**
3. Connect to your mount!

## Common Scenarios

### Scenario A: First Time User
```
Default Profile ? Edit ? Activate ? Restart ? Use
```

### Scenario B: Experienced User
```
Clone Default ? Edit Settings ? Test ? Activate ? Use
```

### Scenario C: Multiple Mounts
```
Create Profile 1 ? Configure
Create Profile 2 ? Configure
Switch: Activate ? Restart
```

## Cheat Sheet

| Task | Action |
|------|--------|
| **Create** | New Profile ? Name ? Mode ? Create |
| **Edit** | Edit ? Change Settings ? Save |
| **Use** | Activate ? **Restart App** |
| **Copy** | Clone ? Edit |
| **Backup** | Export ? Save JSON |
| **Delete** | Delete ? Confirm |

## Important Rules

? **DO**:
- Restart app after activating profile
- Export before major changes
- Use descriptive names
- Clone default profiles to edit

? **DON'T**:
- Edit default profiles (clone them)
- Delete active profile
- Forget to restart after activating

## Next Steps

- Read [User Guide](USER_GUIDE.md) for detailed instructions
- See [Troubleshooting](TROUBLESHOOTING.md) if issues occur
- Review [API](API.md) for programmatic access

## Need Help?

- Can't find your mount? Check Connection tab
- Settings not working? Did you restart?
- Profile disappeared? Click Refresh
- More help: See full [User Guide](USER_GUIDE.md)
