# FP2MusicReplacer

## Installation:
To install the mod extract the downloaded zip file contents from the releases tab into the main game directory.  
If asked, agree to merge the BepInEx folders.  

# Usage:
Place replacement tracks in mod_overrides\MusicReplacements.
The file name must match the name of the track you want to replace - all track names are provided in AudioTrackList.txt

## Supported formats:
~~MPEG layer 3 (.mp3)~~  
Ogg Vorbis (.ogg)  
Microsoft Wave (.wav)  
Audio Interchange File Format (.aiff / .aif)  
Ultimate Soundtracker module (.mod)  
Impulse Tracker module (.it)  
Scream Tracker module (.s3m)  
FastTracker 2 module (.xm)  

# Replacing SFX/Voice:
Place replacement files in mod_overrides\SFXReplacements. The file name must match the name of the sound you want to replace - all SFX names are provided in SFXList.txt

## Supported SFX formats:

Ogg Vorbis (.ogg)

Microsoft Wave (.wav)


All custom SFX files are loaded at launch of the game, which might make it take longer to load.
You can track the loading progress in the console.
If one were to replace all 3000+ sfx files, load time can take up to a minute or so.


## Prerequisites:
The mod requires [BepinEx 5](https://github.com/BepInEx/BepInEx) to function. You can download it here:
* [Direct Download](https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x86_5.4.21.0.zip)  

Extract the downloaded zip file in to the main game directory.  

## Building:
Follow the BepinEx guide for setting up Visual Studio found [here](https://docs.bepinex.dev/master/index.html).  
Open the solution in Visual Studio and build the project.
