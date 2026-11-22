# Hover Click

Hover Click turns a resting pointer into a Windows mouse click. A head mouse, eye tracker or other pointing device supplies the movement. You can operate the toolbar, change settings and resume clicking by dwell alone.

## Start

Extract the complete Windows bundle for your processor and open `HoverPulse.exe`. Keep the supplied DLLs beside it. The bundle includes the .NET runtime. Use one active dwell clicker per Windows sign-in. A separate sign-in lets you try Hover Click while keeping your usual hands-free setup available.

## Use the toolbar

| Control | What to do |
|---|---|
| Pause / Resume | Dwell on Pause to collapse the toolbar. Resume stays in the same place. Move the pointer slightly within Resume and hold it there to start clicking again. |
| Left, Double, Drag, Right | Dwell on an action to use it once. Move away and dwell on the same action again to lock it. A dashed outline and arrow identify the one-use action. A solid outline and padlock identify the default. |
| Drag | Select Drag and dwell on the item to hold the left button. Move the item, then dwell at its destination to drop it. The hand changes appearance while holding. Pausing preserves the hold. |
| Setup | Adjust dwell time, movement tolerance, target size, appearance, labels, padding, sound, floating behavior or input backend. Choose OK to save or Cancel to discard the staged changes. |
| Exit | Dwell on Exit, then confirm by dwelling on Exit Hover Click. |
| Move | Dwell on Move, guide the toolbar to the desired position and hold still to place it. |

The starting appearance uses small black-and-gray targets, icons only and 4 DIP of padding. Setup offers four sizes and three palettes. Turning on labels keeps the targets the same size.

## How a dwell fires

The compatibility profile requests one sample every 100 ms. With `ClickTime=2`, the third qualifying sample after a reset fires. With `MoveLimit=5`, movement of at least five Windows logical units on either axis resets the count and rearms clicking. Keeping the pointer still after a click produces one click. The legacy backend uses `mouse_event`. Setup also offers `SendInput`.

Hover Click saves preferences in `%APPDATA%\HoverPulse\settings.json`. The Bedroom profile button stages the captured compatibility settings for review. Windows applies its normal input restrictions to elevated and secure windows.

The release record identifies the tested Windows builds, executable hashes and remaining qualification work. [Release status](specs/parity-release-status.md)
