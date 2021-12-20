using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;


internal class Printer
{
    //const int GAP = 5;
    private readonly IEnumerable<(XImage front, XImage back)> cards;
    private readonly int WIDTH;
    private readonly int HEIGHT;
    private readonly int GAP;
    private readonly bool printFront;
    private readonly bool printBack;
    private readonly Bleed bleed;
    private readonly PageOrientation orientation;

    public Printer(IEnumerable<(XImage front, XImage back)> result, int width, int height, int gap, bool printFront, bool printBack, Bleed bleed, PageOrientation orientation)
    {
        this.cards = result;
        this.WIDTH = width;
        this.HEIGHT = height;
        this.GAP = gap;
        this.printFront = printFront;
        this.printBack = printBack;
        this.bleed = bleed;
        this.orientation = orientation;
    }

    //const int HEIGHT = 64;
    //const int WIDTH = 41;
    public static Task DoIt(ReadOnlySpan<string> args)
    {
        Consumer consumer = Consumer.Build(true)
                    .OptionalSingel(out var size, new SizeParameter() { Height = 64, Width = 41 })
                    .OptionalSingel(out var gap, new GapParameter() { Width = 5 })
                    .List<FileParameter>(out var file)
                    .List<DirectoryParameter>(out var dir)
                    .List<ZipParameter>(out var zip)
                    .OptionalSingel(out var orientation, new LandscapeParameter() { Orientation = PageOrientation.Portrait })
                    .OptionalSingel(out var bleed, new BleedParameter() { Bleed = Bleed.None })
                    .OptionalSingel(out var printBack, new NoBackParameter() { Print = true })
                    .OptionalSingel(out var printFront, new NoFrontParameter() { Print = true });
        var errors = consumer
            .Run(ref args);

        if (errors.Any())
        {
            foreach (var error in errors)
            {
                Console.Error.WriteLine(error);
            }
            return Task.CompletedTask;
        }

        var cards = Task.WhenAll(file, dir.ContinueWith(x => x.Result as FileParameter[]), zip.ContinueWith(x => x.Result as FileParameter[])).ContinueWith(x => x.Result.SelectMany(x => x.SelectMany(x => x.Images())));

        return cards.ContinueWith(x => Task.Run(() => new Printer(x.Result, size.Result.Width, size.Result.Height, gap.Result.Width, printFront.Result.Print, printBack.Result.Print, bleed.Result.Bleed, orientation.Result.Orientation).DoIt())).Unwrap();
    }
    private void DoIt()
    {
        // Create a temporary file
        string filename = String.Format("{0}_tempfile.pdf", Guid.NewGuid().ToString("D").ToUpper());
        var s_document = new PdfDocument();
        s_document.Info.Title = "Shatterd Accension - Cards";
        s_document.Info.Author = "layouted by tool";
        s_document.Info.Subject = "A collection of all Cards of Shatterd ascenceion";
        s_document.Info.Keywords = "Shatterd Accension - Card collection";


        PdfPage front = null!, back = null!;
        XGraphics gfxFront = null!, gfxBack = null!;
        int cardsPerRow = 0, cardsPerColumn = 0;
        double marginLeft = 0, marginTop = 0;
        double printedWidth = 0, printedHeight = 0;


        void printImage(int x, int y, XImage image, XImage imageBack)
        {


            var halfGap = GAP / 2;
            // Left position in point
            double xPos = GAP + (WIDTH + GAP) * x + marginLeft;
            double xPosBack = back.Width.Millimeter - marginLeft - WIDTH - GAP - ((WIDTH + GAP) * x);
            double yPos = GAP + (HEIGHT + GAP) * y + marginTop;
            gfxFront.DrawImage(image, new XRect(xPos, yPos, WIDTH, HEIGHT), new XRect(0, 0, image.PointWidth, image.PointHeight), XGraphicsUnit.Point);
            if (bleed.HasFlag(Bleed.Back))
                gfxBack.DrawImage(imageBack, new XRect(xPosBack - halfGap, yPos - halfGap, WIDTH + GAP, HEIGHT + GAP), new XRect(0, 0, imageBack.PointWidth, imageBack.PointHeight), XGraphicsUnit.Point);
            else
                gfxBack.DrawImage(imageBack, new XRect(xPosBack, yPos, WIDTH, HEIGHT), new XRect(0, 0, imageBack.PointWidth, imageBack.PointHeight), XGraphicsUnit.Point);

        }




        var currentX = 0;
        var currentY = 0;


        foreach (var card in cards)
        {
            if (currentX == 0 && currentY == 0)
                AddPage(s_document, ref front, ref back, ref gfxFront, ref gfxBack, out cardsPerRow, out cardsPerColumn, out marginLeft, out marginTop, out printedWidth, out printedHeight);
            printImage(currentX, currentY, card.front, card.back);
            currentX++;
            if (currentX >= cardsPerRow)
            {
                currentX = 0;
                currentY++;
            }
            if (currentY >= cardsPerColumn)
            {
                currentY = 0;

            }
        }

        // overide the last free cards with a white box again

        var rowsToClean = cardsPerRow - currentY - 1;

        var columnsToClean = cardsPerColumn - currentX;
        if (currentX == 0)
        {
            columnsToClean = 0;
            rowsToClean++;
            if (currentY == 0)
                rowsToClean = 0;
        }

        //lower columns
        if (columnsToClean > 0)
        {
            if (rowsToClean >= cardsPerRow - 1)
            {
                // we are in the top most row Also remove the last gap
                gfxFront.DrawRectangle(brush: XBrushes.White,
                    x: front.Width.Millimeter - (marginLeft + columnsToClean * (WIDTH + GAP)),
                    y: front.Height.Millimeter - ((HEIGHT + GAP) * (rowsToClean + 1) + marginTop + GAP),
                    width: columnsToClean * (WIDTH + GAP),
                    height: HEIGHT + GAP + GAP);
                gfxBack.DrawRectangle(brush: XBrushes.White,
                    x: front.Width.Millimeter - (marginLeft + cardsPerColumn * (WIDTH + GAP) + GAP),
                    y: front.Height.Millimeter - ((HEIGHT + GAP) * (rowsToClean + 1) + marginTop + GAP),
                    width: columnsToClean * (WIDTH + GAP),
                    height: HEIGHT + GAP + GAP);

            }
            else
            {
                gfxFront.DrawRectangle(brush: XBrushes.White,
                                       x: front.Width.Millimeter - (marginLeft + columnsToClean * (WIDTH + GAP)),
                                       y: front.Height.Millimeter - ((HEIGHT + GAP) * (rowsToClean + 1) + marginTop),
                                       width: columnsToClean * (WIDTH + GAP),
                                       height: HEIGHT + GAP);
                gfxBack.DrawRectangle(brush: XBrushes.White,
                                   x: front.Width.Millimeter - (marginLeft + cardsPerColumn * (WIDTH + GAP) + GAP),
                                   y: front.Height.Millimeter - ((HEIGHT + GAP) * (rowsToClean + 1) + marginTop),
                                   width: columnsToClean * (WIDTH + GAP),
                                   height: HEIGHT + GAP);
            }
        }
        if (rowsToClean > 0)
        {
            gfxFront.DrawRectangle(brush: XBrushes.White,
                                   x: marginLeft,
                                   y: front.Height.Millimeter - ((HEIGHT + GAP) * rowsToClean + marginTop),
                                   width: printedWidth,
                                   height: rowsToClean * (HEIGHT + GAP));
            gfxBack.DrawRectangle(brush: XBrushes.White,
                                   x: marginLeft,
                                   y: front.Height.Millimeter - ((HEIGHT + GAP) * rowsToClean + marginTop),
                                   width: printedWidth,
                                   height: rowsToClean * (HEIGHT + GAP));
        }


        // Save the s_document...
        s_document.Save(filename);
        // ...and start a viewer
        var info = new ProcessStartInfo(filename)
        {
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(info);
    }

    private void AddPage(PdfDocument s_document, [AllowNull] ref PdfPage front, [AllowNull] ref PdfPage back, [AllowNull] ref XGraphics gfxFront, [AllowNull] ref XGraphics gfxBack, out int cardsPerRow, out int cardsPerColumn, out double marginLeft, out double marginTop, out double printedWidth, out double printedHeight)
    {
        if (gfxFront is not null)
            gfxFront.Dispose();
        if (gfxBack is not null)
            gfxBack.Dispose();

        front = s_document.AddPage();
        back = s_document.AddPage();
        if (!printBack) // not efficent but it works...
            s_document.Pages.Remove(back);
        if (!printFront)
            s_document.Pages.Remove(front);

        gfxFront = XGraphics.FromPdfPage(front, XGraphicsUnit.Millimeter);
        gfxBack = XGraphics.FromPdfPage(back, XGraphicsUnit.Millimeter);

        front.Size = back.Size = PdfSharp.PageSize.A4;
        front.Orientation = back.Orientation = orientation;


        cardsPerRow = (int)(front.Width.Millimeter - GAP) / (GAP + WIDTH);
        cardsPerColumn = (int)(front.Height.Millimeter - GAP) / (GAP + HEIGHT);

        if (cardsPerColumn == 0)
            cardsPerColumn = 1;
        if (cardsPerRow == 0)
            cardsPerRow = 1;

        printedWidth = cardsPerRow * (WIDTH + GAP) + GAP;
        printedHeight = cardsPerColumn * (HEIGHT + GAP) + GAP;

        marginLeft = (front.Width.Millimeter - printedWidth) / 2;
        marginTop = (front.Height.Millimeter - printedHeight) / 2;

        XPen black = new(XColors.Black, 0.5);
        for (int i = 0; i < cardsPerColumn; i++)
        {
            var x1 = marginLeft + GAP + (WIDTH + GAP) * i;
            var x2 = x1 + WIDTH;
            var y1 = 0;
            var y2 = front.Height.Millimeter;
            gfxFront.DrawLine(black, x1, y1, x1, y2);
            gfxFront.DrawLine(black, x2, y1, x2, y2);
            gfxBack.DrawLine(black, x1, y1, x1, y2);
            gfxBack.DrawLine(black, x2, y1, x2, y2);

        }
        for (int i = 0; i < cardsPerRow; i++)
        {
            var x1 = 0;
            var x2 = front.Width.Millimeter;
            var y1 = marginTop + GAP + (HEIGHT + GAP) * i;
            var y2 = y1 + HEIGHT;
            gfxFront.DrawLine(black, x1, y1, x2, y1);
            gfxFront.DrawLine(black, x1, y2, x2, y2);
            gfxBack.DrawLine(black, x1, y1, x2, y1);
            gfxBack.DrawLine(black, x1, y2, x2, y2);

        }
        XFont font = new("Verdana", 4, XFontStyle.Regular);
        gfxFront.DrawString((s_document.PageCount / 2).ToString(), font, XBrushes.Black, new XPoint(3, 10));
        gfxFront.DrawRectangle(XBrushes.Black, marginLeft, marginTop, printedWidth, printedHeight);
        gfxBack.DrawRectangle(XBrushes.Black, back.Width.Millimeter - (marginLeft + printedWidth), back.Height.Millimeter - (marginTop + printedHeight), printedWidth, printedHeight);

    }



}
