# Manual Verification (Eclipse UI fixes)

Use these steps after building/running `Froststrap-2.0.0-beta.10`.

## 1) UI cleanup (Settings + navigation)
1. Open Froststrap.
2. Go to **Settings** (main window).
3. Confirm there is **no moving/marquee aurora background** behind the content (it should feel static/clean).
4. In the settings navigation:
   - Confirm **Server Browser** is **not present** anywhere.
   - Confirm **AltMan** is **not present** anywhere.
5. Attempt to open AltMan/Server Browser using any in-app navigation/search entry (if your build still shows one via saved state), and confirm it **does not open**.

## 2) Region selector (RoValra)
1. Open the **Region Selector** page (the page used for datacenter/region server browsing).
2. Confirm the **region combo loads** without showing an error.
3. Choose a region and use **search**.
4. Confirm the server list **returns rows** (not an error/empty state caused by datacenter parsing).

## 3) BetterMatchmaking
1. In **Settings → Behavior/Matchmaking**, enable **Better Matchmaking**.
2. Launch/join a **normal place** (not a VIP access-code join).
3. Observe the resulting join behavior:
   - Confirm the launch lands you in a **different `gameInstanceId`** than the original/default place instance (BetterMatchmaking should rewrite the launch request to a selected server).
4. If you can, compare logs for the selected region/server (or verify by re-opening the same join flow and seeing it pick different server instances).

## 4) Multi-instance
1. Enable **Multi Instance** in Settings.
2. Launch two Roblox clients using the same Eclipse button/flow (start the second while the first is still launching/running).
3. Confirm you **do not** see Roblox’s “previous instance will be closed” behavior.
4. Confirm both clients remain open and usable after both launches complete.

## 5) Native VIP servers list (no WebView)
1. Enable **VIP Server Prompt** in Settings.
2. Start/join a game that has VIP servers.
3. Confirm the **native in-app VIP list dialog** opens (not the old WebView picker).
4. Confirm the dialog header shows the **game name**.
5. Select a VIP server and press **Join**.
6. Confirm the launch lands in the **selected VIP server**.
7. Repeat once and press **Skip**; confirm it launches normally without VIP access code.

