# DualSense HID protocol (as implemented in `DualSenseHid.cs`)

Vendor `0x054C`. Products: `0x0CE6` (DualSense), `0x0DF2` (DualSense Edge). The mod accepts the top-level collection with usage page 0x01 and usage 0x05 (game pad).

**USB or Bluetooth** is detected from HID caps:
- USB: input report length 64, output 48.
- Bluetooth: input 78, output ≥ 78 (the BT descriptor may report a larger maximum).

`WriteFile` must be given exactly `OutputReportByteLength` bytes. The rest is padded with zeros.

## Input
| Transport | Report ID | Data offset `o` |
|---|---|---|
| USB | `0x01` | 1 |
| BT full | `0x31` | 2 |
| BT simple (before feature 0x05 is read) | `0x01` | 1, different layout |

Full layout, relative to `o`:

| Offset | Content |
|---|---|
| 0, 1 | LX, LY (0..255, 128 centre, Y down) |
| 2, 3 | RX, RY |
| 4, 5 | L2, R2 analog |
| 6 | sequence |
| 7 | low nibble = D-pad hat (0 N, 1 NE … 7 NW, 8 none); bits 4–7 = Square, Cross, Circle, Triangle |
| 8 | L1, R1, L2, R2, Create, Options, L3, R3 (bits 0–7) |
| 9 | PS, Touchpad, Mute (bits 0–2) |

The BT simple report has LX LY RX RY at `o+0..3`, buttons at `o+4..6` and L2/R2 at `o+7..8`.

**Switching to full mode:** on Bluetooth, reading feature report `0x05` (calibration) with `HidD_GetFeature` switches the controller to full `0x31` reports.

## Output: the 47-byte common block

| Offset | Field |
|---|---|
| 0 | valid_flag0: `0x01` compatible vibration, `0x02` haptics select, `0x04` right-trigger effect, `0x08` left-trigger effect |
| 1 | valid_flag1: `0x04` lightbar, `0x10` player LEDs, `0x01` mic LED, `0x02` power save |
| 2 | right motor (high frequency, small) |
| 3 | left motor (low frequency, big) |
| 10–20 | R2 effect (11 bytes) |
| 21–31 | L2 effect (11 bytes) |
| 38 | valid_flag2: `0x02` lightbar setup |
| 41 | lightbar setup: `0x02` = release the default blue fade (sent once when the mod takes over the lightbar) |
| 44–46 | lightbar R, G, B |

How the block is wrapped:
- **USB:** `[0x02] + common`.
- **BT:**
  - `[0x31, seq<<4, 0x10] + common`, zero-padded.
  - Bytes 74–77 hold the CRC32, little-endian.
  - The CRC is standard CRC-32 (poly 0xEDB88320, init/xorout 0xFFFFFFFF), computed over the seed byte **0xA2** followed by bytes 0..73.

## Adaptive trigger effects (11 bytes)
Zones are 0..9 along the trigger travel. A strength or amplitude of 1..8 is stored as 3 bits (value − 1) per zone.

| Mode | Bytes |
|---|---|
| `0x05` Off | — |
| `0x21` Feedback (resistance) | [1..2] active-zone mask u16 LE, [3..6] packed strengths u32 LE |
| `0x26` Vibration | [1..2] active-zone mask, [3..6] packed amplitudes, [9] frequency (Hz) |

`OutputBuilder.TriggerFeedback(int[10])` supports per-zone strengths; the brake pedal uses this for progressive resistance.

References: Linux `drivers/hid/hid-playstation.c`, DS4Windows `DualSenseDevice`, Nielk1's `TriggerEffectGenerator` notes.
