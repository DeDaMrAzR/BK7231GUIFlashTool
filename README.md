# BK7231 GUI Flash Tool

BK7231 GUI Flash Tool a simple Windows application that allows you to flash the OpenBK firmware to Beken chipset (BK7231T or BK7231N, can also flash BK7231M, BL2028N and [BK7238](https://www.elektroda.com/rtvforum/topic4092465.html)) based devices without having extensive programming knowledge.

Futhermore, it automatically creates an original firmware backup, and uses it to extract GPIO configuration that you can later import in OBK Web Application (Import Tab).

❗ NOTE: The flash dump may contain your SSID and pass if the device was paired at the time of the backup

[See also Russian guide for this tool and BK7231N.](https://www.v-elite.ru/t34)

# [Youtube Tutorial for example usage - CB2S flashing](https://youtu.be/YQdR7r6lXRY?list=PLzbXEc2ebpH0CZDbczAXT94BuSGrd_GoM)
See also the secondary example: [WB3S flashing](https://youtu.be/-a5hV1y5aIU?list=PLzbXEc2ebpH0CZDbczAXT94BuSGrd_GoM).

Per device flashing guides (NOTE: they may use obsolete flash tools, so always prefer to use new tool from this repo):
- [BK7231T/WB3S flashing guide - 2g Tuya wall switch - with SOIC8 chip desoldering - Home Assistant](https://www.youtube.com/watch?v=Yb3zXtBdSnE)
- [Tuya Relay CB2S/BK7231N control without Local Tuya - 100% free from cloud with Home Assistant guide](https://www.youtube.com/watch?v=PKkiqDNFIx8)
- [How to add IR receiver and extra buttons to any Tuya BK7231T/BK7231N LED strip controller, 100% DIY](https://www.youtube.com/watch?v=KU0tDwtjfjw)
- [RGBCW Tuya bulb flashing guide - BK7231N (WB2L_M1) - Tasmota/ESPHome multiplatform replacement](https://www.youtube.com/watch?v=2e1SUQNMrgY)
- [TreatLife Intertek teardown & programming tutorial - WB3S/BK7231 - 100% local Home Assistant control](https://www.youtube.com/watch?v=-a5hV1y5aIU)
- [Firmware change process for RGB+CCT Tuya ceiling lamp, OpenBeken, WiFi module desoldering, BK7231N](https://www.youtube.com/watch?v=YQdR7r6lXRY)

See also our [youtube channel](https://www.youtube.com/@elektrodacom) and [forum](https://www.elektroda.com/)

# Compiling and Running on Linux

It should be possible to compile and run this tool on Linux by using [Mono](https://www.mono-project.com/). Mono is an open-source implementation of the .NET Framework which is also sponsored by Microsoft.

Please note that if you're opting to run this tool under Linux, it is **highly** recommended for you to install Mono using your distro's package manager.

Once it's installed, you can compile this software by executing `xbuild` on the project directory. To execute the program, you can simply execute the following command:

`mono BK7231Flasher/bin/debug/BK7231Flasher.exe`

# Brief usage instructions

1. Connect UART to USB converter to Beken TXD1/RXD1
2. Start flasher tool
3. Select N or T platform
4. Click "Download latest from web" to get firmware binary
5. Click "Do backup and flash new"
6. Reset/repower Beken
7. Tool will do both read and flash in one row. 
8. Done!

No command line and no strange arguments required.

# Detailed usage instructions

1. Download and unpack executable from Releases tab on the right
2. Prepare flashing circuit for BK7231 (both T and N)

    - Get a USB to UART bridge with 3.3V voltage signals
    - Connect the Bridge RX to Module TXD1, and Bridge TX to Module RXD1
    - If necessary, solder a wire to the CEN pad (if you want to RESET through shorting CEN to ground)
    - Power the device from either the bridge or an external power source. All Beken based modules require 3.3v.  
      ⚠️ **NEVER try hacking devices while connected to mains power!** 

3. Open our flasher:

![image](https://user-images.githubusercontent.com/85486843/210281085-6141160b-df6d-486c-b574-ef784f5cbd56.png)

4. Select proper platform - BK7231T or BK7231N
5. Select your COM port of USB to UART converter
6. Click "Download latest from Web" to get proper binary file
7. Wait for download to finish

![image](https://user-images.githubusercontent.com/85486843/210281125-a3e25ab2-3144-4e02-a30c-6e135ecefd24.png)

8. Close download window
9. Click "Backup and flash new"
10. When the log window is waiting for "Getting bus", do a device reboot/reset. You can do this in two ways, choose one:

    - **Option A:** short CEN to GND for 0.25s (it is tricky to get this right, requires precise timing)
    - **Option B:** power off and on device (of course, it should not be connected to mains, use your own safe 3.3V power supply that can supply enough current)
  
![image](https://user-images.githubusercontent.com/85486843/210281194-27decf09-723e-41f7-8b47-6fe2b6bb4857.png)

11. It will begin reading (it does first backup, then write)

![image](https://user-images.githubusercontent.com/85486843/210281251-cd69ddab-f0ab-4389-8476-0eb33045aa76.png)

12. After reading, it will start the new firmware erase

![image](https://user-images.githubusercontent.com/85486843/210281467-10129860-61da-4420-a9aa-9910f0e57099.png)

13. And then, automatically, write:

![image](https://user-images.githubusercontent.com/85486843/210281482-0eb62054-f44e-4c10-959a-65f4147cefca.png)

14. Done:

![image](https://user-images.githubusercontent.com/85486843/210281504-b592db7d-9e6e-47f9-81fc-3619a2f00204.png)

15. Firmware access point show appear now. Connect to it and enter 192.168.4.1 configuration page.
16. Remember that saved firmware backup is in the "backups" dir

# CRC Mismatch?
CRCs are calculated correctly for both N and T. If you get CRC mismatch, you are most likely selecting a wrong chip type.
![image](https://user-images.githubusercontent.com/85486843/210281290-31d037f5-61c1-403b-a9c5-891fbda75914.png)

# OBK Configuration via UART
See this tutorial:
https://www.elektroda.com/rtvforum/viewtopic.php?p=20733610#20733610

# Can't auto download firmware?
Firmware download will not work on systems without newer TLS version required by Github. You can always manually download release from here:
https://github.com/openshwprojects/OpenBK7231T_App
and put into firmwares bin, then restart flasher.

# Automatic reboot on read/write (so you don't have to power cycle manually)
This tool supports automatic reboot command, just like bkWriter 1.60, but you have to enable UART command line in OBK for it first:
![image](https://github.com/openshwprojects/BK7231GUIFlashTool/assets/85486843/c63a163f-b1be-4f61-80aa-b161f7c706bd)
With this option enabled, OBK will receive the "reboot" string send by flasher on UART before any read/write operation is started and will automatically get bus.

# Other problems?
You can also try changing the baudrate for flashing. Remember - sometimes higher baud rate might worker better than lower one!

If you still need help, you can ask on our forums: https://www.elektroda.com/
