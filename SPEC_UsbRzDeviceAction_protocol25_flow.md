# UsbRzDeviceAction — Protocol 2.5 HID command flow spec

Source of truth: `electron/UsbRzDeviceAction.js`

---

## Data-length rules (sendFeatureReport / sendFeatureReportMutex)

| `dataSend` length | Detected as          | Wire bytes sent | Notes                                                                                                  |
| ----------------- | -------------------- | --------------- | ------------------------------------------------------------------------------------------------------ |
| 90                | Protocol 2.5 (auto)  | **91**          | `reportId` (or `claimInterface` if `reportId` absent) is prepended as byte 0                           |
| 91                | Protocol 2.5 (as-is) | **91**          | Already includes the leading report-ID byte; sent unchanged                                            |
| 65                | Protocol 2.5 (65 B)  | **65**          | `data.protocol` **must** be explicitly set to `'25'`; auto-detection does NOT apply to 65-byte packets |

`getFeatureReport` reads back **91 bytes** by default (`reportLength ?? 91`). For 65-byte devices, pass `reportLength: 65` explicitly.

---

## Normal protocol command flow (one command)

```
Upper layer                      Electron main (UsbRzDeviceAction)
     |                                          |
     |--- hid.sendFeatureReportMutex ---------->|
     |                                          |  1. acquires device mutex
     |                                          |  2. if previous command was a batch (isLastCommandBatch=true),
     |                                          |     waits up to sleepTimeBetweenOut ms (default 5 ms)
     |                                          |     before sending
     |                                          |  3. sends feature report (OUT)
     |                                          |     - 90-byte dataSend → prepend reportId → send 91 bytes
     |                                          |     - 91-byte or 65-byte dataSend → send as-is
     |                                          |  4. sets isLastCommandBatch = false
     |                                          |  *** mutex is NOT released here ***
     |<-- return sendLength --------------------|
     |                                          |
     |--- hid.getFeatureReport --------------->|
     |                                          |  5. mutex already held — no re-acquire
     |                                          |  6. reads feature report (IN), default 91 bytes
     |                                          |     (pass reportLength: 65 for 65-byte devices)
     |                                          |  *** mutex is NOT released here ***
     |<-- return response data (slice [1:]) ----|
     |                                          |
     |--- hid.releaseMutex ------------------->|
     |                                          |  7. releases device mutex
     |<-- return true --------------------------|
```

---

## Lighting batch flow (sendFeatureReportInBatch)

```
Upper layer                      Electron main (UsbRzDeviceAction)
     |                                          |
     |--- hid.sendFeatureReportInBatch ------->|
     |                                          |  1. acquires mutexLockBatch (lightweight guard)
     |                                          |  2. checks if main device mutex is locked
     |                                          |     - if locked: increments mutexLockBatchCount
     |                                          |       - if mutexLockBatchCount > 2: drops frame, returns
     |                                          |  3. releases mutexLockBatch
     |                                          |  4. acquires main device mutex
     |                                          |  5. sends batch frame via hidDevice.sendFeatureReportInBatch
     |                                          |  6. sets isLastCommandBatch = true
     |                                          |  7. sleep(2 ms)
     |                                          |  8. ALWAYS releases main mutex in finally
     |                                          |     (unlike sendFeatureReportMutex, does NOT hold it)
     |                                          |  9. decrements mutexLockBatchCount if it was incremented
     |<-- return res ---------------------------|
```

---

## Mixed flow — protocol commands and lighting frames interleaved

Protocol commands and lighting frames arrive concurrently in any order. The single per-device mutex serialises them.

```
Time →   [sendFeatureReportMutex]──────hold──────[getFeatureReport]──[releaseMutex]
                │                                                           │
                │  batch arrives → mutex locked                             │
                │  mutexLockBatchCount++ (1)                                │
                │                                                           │
                │  2nd batch → mutex locked                                 │
                │  mutexLockBatchCount++ (2)                                │
                │                                                           │
                │  3rd batch → count > 2 → DROPPED                         │
                │                                                           │
                │                                         mutex released ──>│
                │                                    batch1 acquires mutex  │
                │                                    sends frame            │
                │                                    releases mutex         │
                │                                    batch2 acquires mutex  │
                │                                    sends frame            │
                │                                    releases mutex         │
```

**Key invariants:**

- `hid.sendFeatureReportMutex` **holds** the mutex across the OUT call — the mutex is only freed by the subsequent `hid.releaseMutex` call.
- `hid.getFeatureReport` **assumes** the mutex is already held; it neither acquires nor releases it.
- `hid.sendFeatureReportInBatch` **always releases** the mutex in its `finally` block immediately after sending.
- If the mutex timeout (500 ms, `mutexSendTimeout`) fires while waiting in `sendFeatureReportInBatch`, the caught `E_TIMEOUT` is logged and the frame is discarded without corrupting `mutexRelease`.
- `mutexLockBatchCount` is protected by the secondary `mutexLockBatch` mutex, preventing count drift under concurrent batch arrivals.
- `sleepTimeBetweenOut` (default **5 ms**, configurable per command via `DeviceCmdData.sleepTimeBetweenOut`) adds a guard delay at the start of `sendFeatureReportMutex` when the preceding command was a batch frame (`isLastCommandBatch === true`), giving the device time to flush the batch before accepting a protocol command.

---

## Summary table — mutex lifecycle per action

| Action                         | Acquires mutex    | Holds mutex after return | Releases mutex                    |
| ------------------------------ | ----------------- | ------------------------ | --------------------------------- |
| `hid.sendFeatureReport`        | No                | —                        | No                                |
| `hid.sendFeatureReportMutex`   | Yes               | **Yes**                  | No — must call `hid.releaseMutex` |
| `hid.getFeatureReport`         | No (assumes held) | Yes (unchanged)          | No                                |
| `hid.releaseMutex`             | —                 | No                       | **Yes**                           |
| `hid.sendFeatureReportInBatch` | Yes               | No                       | **Yes** (in `finally`)            |
| `hid.acquireMutex`             | Yes               | **Yes**                  | No — must call `hid.releaseMutex` |

---

## What happens if `hid.releaseMutex` is never called?

The mutex is **not automatically released on a timer**.

`withTimeout(new Mutex(), 500)` only affects callers that are **waiting to acquire** the lock:
- If the lock is free, `acquire()` succeeds immediately with no timeout involved.
- If the lock is already held, a new caller waits up to 500 ms (`mutexSendTimeout`). If the lock is still not released after 500 ms, `acquire()` throws `E_TIMEOUT` and the waiting caller gives up.
- The **current holder** is completely unaffected by the timeout — it keeps the lock until it explicitly calls the release function. There is no forced eviction.

However, two safety paths exist that will force-release a stuck lock:

### Path 1 — Next caller times out (500 ms)

`deviceGet.mutex` is created as `withTimeout(new Mutex(), 500)` (`mutexSendTimeout`).

```
Caller A: sendFeatureReportMutex → acquires mutex, sets deviceGet.mutexRelease = releaseA
          ... never calls hid.releaseMutex ...

Caller B: sendFeatureReportMutex → tries mutex.acquire()
          waits up to 500 ms ...
          E_TIMEOUT thrown — deviceGet.mutexRelease is still releaseA (acquire() threw before overwriting it)

          catch (e === E_TIMEOUT && deviceGet.mutexRelease):
            deviceGet.mutexRelease()   ← force-releases Caller A's stuck lock
            deviceGet.mutexRelease = null

          Caller B returns undefined (does NOT retry the send)
```

**Effect:** The device is unblocked after at most 500 ms, but Caller B's command is **lost** — the upper layer must retry.

### Path 2 — Window/tab that holds the lock is destroyed

When the renderer window that issued `sendFeatureReportMutex` is destroyed, `windowEvent.tabDestroyed` fires. The constructor handler scans `deviceMap` for any entry whose `mutexUrl` matches the destroyed window's URL and immediately calls `mutexRelease()`:

```javascript
// constructor
globalNodeVar.appEvent.on(windowEvent.tabDestroyed, (event) => {
  for (const [k, v] of this.deviceMap.entries()) {
    if (v.mutexUrl === event.url) {
      if (v.mutexRelease) {
        v.mutexRelease();
        v.mutexRelease = null;
        v.mutexUrl = '';
      }
    }
  }
});
```

**Effect:** The mutex is freed immediately and synchronously when the owning window closes, with no 500 ms wait.

### Summary

| Scenario                                                                | How mutex is freed                                        | Latency          |
| ----------------------------------------------------------------------- | --------------------------------------------------------- | ---------------- |
| `hid.releaseMutex` called normally                                      | Explicit release                                          | Immediate        |
| `hid.releaseMutex` never called, next caller arrives                    | `E_TIMEOUT` in next caller's catch fires `mutexRelease()` | Up to 500 ms     |
| `hid.releaseMutex` never called, owning window destroyed                | `tabDestroyed` event fires `mutexRelease()`               | Immediate        |
| `hid.releaseMutex` never called, no further callers, window still alive | Mutex stays locked indefinitely                           | Never auto-freed |

---

## Debug logging

Mutex lifecycle logging per product ID can be enabled via the `--debugmutex` command-line flag.
See `electron/SPEC_cmdline.md` for details.
