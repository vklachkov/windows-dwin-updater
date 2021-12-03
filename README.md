# Dwin Updater

## Description

Dwin Updater is a console utility for correctly updating hmi panels from dwin via UART

### Set of utilities

Main utility is the dwin updater, but in the process of experiments and research, were written two auxiliary utilities: dump grabber through the local bridge of com ports and dump uploader

## How it works

### Vocabulary

Controller is the program

Screen is the dwin t5l

### About memory structure

The monitor's memory structure is physically divided into 64 parts of 256kb each. Due to the addressing limitation, only 32kb can be written at a time, which leads to the division of each part into 8 32kb segments.

![](https://github.com/zgdump/windows-dwin-updater/blob/main/Data/Flash%20structure.png)

Each file has its own index. It is acceptable, if the file is large, to occupy several parts. For example, this is true for a font file, which, as a rule, takes 11 parts

### General algorithm

The UART software update goes through several stages cyclically. First you need to upload a part of the file, with a maximum size of 32 kb. Then send a command to write data from RAM to a spi flash. And repeat again if the file is larger than 32kb. At the end, send a reset command

### Practical implementation

#### Write to RAM

To write to the monitor's RAM , the controller sends:

`0x5A 0xA5 <N> 0x82 <AH> <AL> ...`

- N packet size, excluding magic bytes and the size itself
- AH is the highest byte of the 16 bit address
- AL is the lowest byte of the 16 bit address

The maximum length of bytes sent is 252 bytes (0xFC). But in practice, as well as according to examples from Chinese documentation, a maximum size of 240 bytes (0xF0) is used

Dwin uses word addressing. The word length is 16 bits. As a rule, during operation, the monitor uses memory addresses 0x0000-0x7999. And 0x8000-0xFFFF leaves unused

Example - the controller sends:

`0x5A 0xA5 0x06 0x82 0x80 0x00 0xAA 0xBB 0xCC`

The monitor will write the value 0xAABBCC to its RAM at 0x8000

### Write to ROM

To write to the monitor spi flash , the controller sends:

`0x5A, 0xA5, 0x0F, 0x82, 0x00, 0xAA, 0x5A, 0x02, SH, SL, AH, AL, 0x17, 0x70, 0x00, 0x00, 0x00, 0x00`

- SH is the highest byte of the 16 bit segment address
- SL is the lowest byte of the 16 bit segment address
- AH is the highest byte of the 16-bit address where the data lies
- AL is the lowest byte of the 16 bits of the address where the data lies

Example - the controller sends:

`0x5A, 0xA5, 0x0F, 0x82, 0x00, 0xAA, 0x5A, 0x02, 0x01, 0x00, 0x80, 0x00, 0x17, 0x70, 0x00, 0x00, 0x00, 0x00`

The monitor will write to a 32kb flash drive, read from RAM at 0x8000, in segment 256 (0x100). That is, the first segment of the file at index 32 (32 * 8 = 256)

##### Status check

The official utility checks the recording completion status after sending the command. But in practice, writing to a spi flash drive occurs only after a reset. It is not known whether dwin can start writing to the flash drive at the time of the update

To check the status, the controller sends:

`0x5A, 0xA5, 0x04, 0x83, 0x00, 0xAA, 0x01`

If the recording is completed, the monitor sends:

`0x5A, 0xA5, 0x06, 0x83, 0x00, 0xAA, 0x01, 0x00, 0x02`

In case of ongoing recording, the monitor sends:

Is unknown. This is not mentioned in the documentation. It is unclear whether such a situation is possible at low uart speeds

### Recording large files

The information was obtained in the process of reverse engineering

The monitor considers the file updated immediately after recording the very first segment. All subsequent segments will be ignored, garbage will be written instead. Sequential updating of the file leads to such glitches:

![](https://github.com/zgdump/windows-dwin-updater/blob/main/Data/Glitches.jpg)

As can be seen from the example of background images, there is only a logo, without page backgrounds. For a correct update, it is required to load the pieces in reverse order, from the final and to the first

## Development process

This section provides general information and documents the tools used

Initially, the information was obtained from official documentation:

[T5L DGUSII Application Development Guide.pdf](https://github.com/zgdump/windows-dwin-updater/blob/main/Data/T5L%20DGUSII%20Application%20Development%20Guide.pdf)

She did not describe the entire update process, but indicated which teams were responsible for it. A detailed description of the update process, with examples, was found in documents from the official Dwin forum

Forum thread:

http://forum.dwin.com.cn/forum.php?mod=viewthread&tid=2753&extra=&page=1

Files:

[T5L OTA ICL Dump.pdf](https://github.com/zgdump/windows-dwin-updater/blob/main/Data/T5L%20OTA%20ICL%20Dump.pdf)

[T5L OTA.docx](https://github.com/zgdump/windows-dwin-updater/blob/main/Data/T5L%20OTA.docx)

The original implementation described in the documents worked only partially. There were no backgrounds, but most of the resources and logic worked flawlessly. I could not find more information on the update process

Dwin as part of Dgus II supplies the UART Download utility, which correctly updates files on the monitor. The next step was to figure out how she does it

First, the option to remove dumps using Device Monitoring Studio was tried. The utility lost bytes during monitoring, and the received dumps did not match one another

The next step, it was decided to simulate the screen responses and get a dump for analysis on the local computer. Without a real device, excluding any external factors. The Virtual Serial Port Tool program was found, with the help of which it turned out to create a Local Bridge. I wrote a utility that read bytes from a given virtual COM port and sent the expected responses. Unfortunately, the created local bridge is unstable and loses data. But after several attempts, as a rule, it was possible to get the correct dumps. For the tests, a third utility was written to load the received dumps into the monitor

Initially, I could not determine in what order the official utility sends parts of the file. I determined this using a regular hex editor through a search, and used 010 editor and winmerge to compare binary files.

## Restrictions

The current OTA implementation has two limitations:
1) There is no way to verify the checksum of downloaded files
2) There is no documentation for updating the operating system

### Checksums

One Chinese programmer wrote an alternative solution:

http://forum.dwin.com.cn/forum.php?mod=viewthread&tid=4084&extra=page%3D1

The disadvantage of such a solution is the absence of the source code of the program for windows, as well as the binary, which must be flashed using sd card

### OS Update

Updating the operating system by UART is hypothetically possible. To do this, there are commands for direct recording in NOR Flash. But neither dumps nor examples were found by me

## Other

Additionally, I will save a link to the branch of the Russian forum. In it, people discussed some screen commands, and also provided links to the Chinese version of the dwin website, where you can find up-to-date documentation

http://arduino.ru/forum/apparatnye-voprosy/dwin-dgus-displei-hmi
