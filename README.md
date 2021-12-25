# TI-SA-PrintSheet-Generator
A tool created to layout images of Twilight Imperium Shatered Ascension. Technically it should work with other cards as
long as they are provided in a similar format.

The tool layouts cards on paper with a black gap between the cards. It will add lines at the edges to help you cut the cards.
It's support for duplex meaning the back of the card is printed on the even pages the front on the odd.

You can read singel files, folders or zip files containing files. Add the corresponding arguments to the call of the executable

- **Singel file**  
  ```
  -file <format paramgeter>
  ```
- **Folder**  
  ```
  -dir <format paramgeter>
  ```
- **Singel file**  
  ```
  -zip <format paramgeter>
  ```

You can use multiple files, folders and zips in one execution.


There are three formats that are supported

- **Singel Fle**  
  A series of files each file is a card. The first image will be the back of the cards. So you need at least two files.
  ```
    images <path to back side>  <path to front> [...<path to front>]
  ```
  If this format is used with dir or zip where already multiple files are read instead provide the name of the back
  side file. You can use a substring of the name as long as it matches only one file.
  ```
    images <path>  <back side name>
  ```

  backSearchPattern
- **Multi Image files**  
  An image that contains all files in a grid. in addition to the path you need the images per row and the images per column.
  Optionally you can set the index of the back side (`0` is the default).
  ```
    multiimages <path> <images per row> <images per column> [<index of the back side>]
  ``` 
- **PDF**  
  An PDF where every page is one card. Optionally you can set the index of the back side (`0` is the default). 
  Instead of an index you can set `after` in that case after every card comes its back side.
  ```
    pdf <path to pdf> [<index of the back side>|after]
  ``` 

  **Example**
  
  ```bash
  ti-sa-printsheet-generator -file images ./deck1/back.png ./deck1/card1.png ./deck1/card2.png -zip pdf strategys.zip after
  ```

  This will make two cards from the folder deck1 using back.png as the background and all pdfs from strategys.zip.
  
## Additional parameters

- **Size**  
  Use `-size <width> <height>` to set the width and hight of the card in mm. (default 64 mm x 41 mm)

- **Gap**  
  Use `-gap <width>` to set the width of the gap between the cards (default 5 mm)

- **Orientation**  
  Use `-landscape` to set the orientation to landscape otherwise its protrait.

- **Bleed**  
  Use `-bleed front|back|both` to enable bleed on the front side, back or both. Bleed will print the image bigger instead of using black gap.
  The image will consume half the bleed around the card. So the image must have a width and height that is 5 mm than the card size when using 
  the default gap.
  
- **No Back side**  
  Use `-no-back` to not print the back side.
  
- **No Front side**  
  Use `-no-front` to not print the front side.
  
  


