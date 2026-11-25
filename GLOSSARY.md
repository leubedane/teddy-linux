# Glossary

## Tonie

The audio content written for one tag UID. However many audio files and folders were selected, adding content always produces exactly one Tonie.

## Track

One MP3/Ogg audio file that becomes a chapter of a Tonie.

_Avoid_: chapter (that is the Toniebox's view of a Track), song, file

## Source folder

A folder the user picked (or dropped) to supply Tracks. Every MP3/Ogg file inside it, including those in subfolders, becomes a Track. Other files are ignored.

## Track order

The sequence in which a Tonie's Tracks are played. The user settles it in the **Sort tracks** dialog before the Tonie is encoded.

## Sort criterion

A rule that sets the whole Track order in one step:

- **Folder, then name**: Tracks are grouped by the folder they are in. Folders come in natural order, and Tracks within each folder are in natural name order. This is the default.
- **Name**: every Track in natural name order, ignoring folders.
- **ID3 track number**: the track number stored in the file's tags. Tracks without one come last, in natural name order.
- **Order added**: the order in which files and folders were added.

Applying a Sort criterion replaces any manual moves.

## Natural order

Names are compared with their numbers read as numbers, so "Folge 2" comes before "Folge 10".
