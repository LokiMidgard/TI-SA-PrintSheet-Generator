using PdfSharp.Drawing;

using SharpCompress.Common;
using SharpCompress.Readers;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


public class Consumer
{


    private Consumer(bool consumeAll)
    {
        this.consumeAll = consumeAll;
    }

    private readonly List<string> errors = new();

    private delegate bool Work(ref ReadOnlySpan<string> args);
    private readonly List<Work> funcs = new();
    private readonly List<Action> finish = new();
    private readonly bool consumeAll;

    public static Consumer Build(bool consumeAll = true)
    {
        var c = new Consumer(consumeAll);
        return c;
    }

    public string[] Run(ref ReadOnlySpan<string> args)
    {
        var oldLength = args.Length;

        while (args.Length > 0)
        {


            foreach (var f in funcs)
            {
                if (f(ref args))
                    break;
            }

            if (oldLength == args.Length && args.Length > 0)
            {
                if (!consumeAll)
                    break;
                // discard the first parameter
                errors.Add($"Unknwon paramter {args[0]}");
                args = args[1..];
            }
            oldLength = args.Length;

        }


        foreach (var f in finish)
        {
            f();
        }




        return errors.ToArray();
    }

    public Consumer RequireSingel<T>(out Task<T> result)
            where T : IConsumer<T>
    {
        var task = new TaskCompletionSource<T>();
        result = task.Task;
        finish.Add(() =>
        {
            if (!task.Task.IsCompleted)
            {
                errors.Add($"Required {T.Argument} was not provided");
                task.SetException(new ArgumentNullException($"Required {T.Argument} was not provided"));
            }
        });
        funcs.Add((ref ReadOnlySpan<string> args) =>
        {
            if (args.Length > 0 && args[0].AsSpan(1).Equals(T.Argument, StringComparison.OrdinalIgnoreCase))
            {
                args = args[1..];
                var r = T.Consume(ref args);
                if (!task.TrySetResult(r))
                {
                    errors.Add($"Skip {r} only one parameter of this type supported");
                }
                return true;
            }
            return false;

        });
        return this;
    }

    public Consumer OptionalSingel<T>(out Task<T> result, T defaultValue)
        where T : IConsumer<T>
    {
        var task = new TaskCompletionSource<T>();
        result = task.Task;
        finish.Add(() =>
        {
            if (!task.Task.IsCompleted)
            {
                task.SetResult(defaultValue);
            }
        });
        funcs.Add((ref ReadOnlySpan<string> args) =>
        {
            if (args.Length > 0 && args[0].AsSpan(1).Equals(T.Argument, StringComparison.OrdinalIgnoreCase))
            {
                args = args[1..];
                var r = T.Consume(ref args);
                if (!task.TrySetResult(r))
                {
                    errors.Add($"Skip {r} only one parameter of this type supported");
                }
                return true;
            }
            return false;

        });
        return this;
    }

    public Consumer List<T>(out Task<T[]> result, int required = 0)
            where T : IConsumer<T>
    {
        var task = new TaskCompletionSource<T[]>();
        var results = new List<T>();
        result = task.Task;
        finish.Add(() => task.SetResult(results.ToArray()));
        funcs.Add((ref ReadOnlySpan<string> args) =>
        {
            if (args.Length > 0 && args[0].AsSpan(1).Equals(T.Argument, StringComparison.OrdinalIgnoreCase))
            {
                args = args[1..];
                var r = T.Consume(ref args);
                results.Add(r);
                return true;
            }
            return false;

        });
        return this;
    }

}

public interface IConsumer<T> where T : IConsumer<T>
{
    static abstract T Consume(ref ReadOnlySpan<string> input);
    static abstract string Argument { get; }
}


public abstract class ConsumerBase
{

    private protected static bool Consume<TC>(ref ReadOnlySpan<string> input, [NotNullWhen(true), MaybeNullWhen(false)] out TC result)
    {
        if (input.Length > 0)
        {
            var first = input[0];
            if (first.StartsWith('-'))
            {
                // ignore other parameter
                result = default;
                return false;
            }
            if (typeof(TC) == typeof(string) && first is TC x)
            {
                input = input[1..];
                result = x;
                return true;
            }
            if (typeof(TC) == typeof(int) && int.TryParse(first, out var i) && i is TC x2)
            {
                input = input[1..];
                result = x2;
                return true;
            }
            if (typeof(TC).IsEnum && Enum.TryParse(typeof(TC), first, true, out var e) && e is TC x3)
            {
                input = input[1..];
                result = x3;
                return true;
            }

            result = default;
            return false;

        }
        else
        {
            result = default;
            return false;
        }
    }
}



public enum ImageFormat
{
    Pdf,
    Images,
    MultiImages
}

public class FileParameter : ConsumerBase, IConsumer<FileParameter>
{
    public static string Argument => "File";

    private readonly Func<IEnumerable<(XImage front, XImage back)>> images;

    private protected FileParameter(Func<IEnumerable<(XImage front, XImage back)>> images)
    {
        this.images = images;
    }

    public IEnumerable<(XImage front, XImage back)> Images() => images();


    public static FileParameter Consume(ref ReadOnlySpan<string> input)
    {

        if (!Consume<ImageFormat>(ref input, out var format))
            throw new ArgumentException("Image format expected as secondparameter");

        if (!Consume<string>(ref input, out var path))
            throw new ArgumentException("Path to zip Expected as first parameter");




        switch (format)
        {
            case ImageFormat.Pdf:
                if (!Consume<int>(ref input, out var backIndex))
                    backIndex = 0;

                return new FileParameter(() =>
                {
                    return GetCardsFromPdf(path, backIndex);
                });
            case ImageFormat.Images:

                var pathes = new List<string>
                {
                    path
                };

                while (Consume<string>(ref input, out var current))
                    pathes.Add(current);

                if (pathes.Count < 2)
                    throw new ArgumentException("Not enought images. required minimum 2");


                return new FileParameter(() => GetCardsFromFiles(pathes.ToArray()));


            case ImageFormat.MultiImages:
                if (!Consume<int>(ref input, out var imagePerRow))
                    throw new ArgumentException("Image back name pattern expected");

                if (!Consume<int>(ref input, out var imagePerColumn))
                    throw new ArgumentException("Image back name pattern expected");

                if (!Consume<int>(ref input, out backIndex))
                    backIndex = 0;

                return new FileParameter(() =>
                {
                    return GetCardsFromImage(path, imagePerRow, imagePerColumn, backIndex);
                });
            default:
                throw new ArgumentException("Unknowin ImageFormat");

        }


    }


    protected private static IEnumerable<(XImage front, XImage back)> GetCardsFromPdf(string path, int backIndex = 0)
    {
        using XPdfForm form = XPdfForm.FromFile(path);
        using XPdfForm back = XPdfForm.FromFile(path);

        back.PageNumber = backIndex + 1;



        for (int i = 1; i <= form.PageCount; i++)
        {
            if (i == backIndex + 1)
                continue;
            form.PageNumber = i;
            yield return (form, back);
        }
    }

    protected private static IEnumerable<(XImage front, XImage back)> GetCardsFromFiles(ReadOnlyMemory<string> pathes)
    {
        //var back = @"C:\Users\patri\source\repos\TI-SA-PrintSheet-Generator\TI-SA-PrintSheet-Generator\Images/Backs/AC_BACK copy.jpg";

        var backPath = pathes.Span[0];
        var rest = pathes[1..];


        using XImage back = XImage.FromFile(backPath);

        for (int i = 0; i < rest.Length; i++)
        {
            var path = rest.Span[i];

            using XImage image = XImage.FromFile(path);
            yield return (image, back);
        }
    }

    protected private static IEnumerable<(XImage front, XImage back)> GetCardsFromImage(string path, int cardCountWidth, int cardCountHeight, int backImageIndex)
    {
        using Image src = Image.FromFile(path);

        var cardWidth = src.Width / cardCountWidth;
        var cardHeight = src.Height / cardCountHeight;
        using var back = new Bitmap(cardWidth, cardHeight);

        using (var gfx = Graphics.FromImage(back))
            gfx.DrawImage(src, new Rectangle(0, 0, cardWidth, cardHeight), new Rectangle((backImageIndex % cardCountWidth) * cardWidth, (backImageIndex / cardCountWidth) * cardHeight, cardWidth, cardHeight), GraphicsUnit.Pixel);

        using var backStream = new MemoryStream();
        back.Save(backStream, System.Drawing.Imaging.ImageFormat.Png);
        using var xBack = XImage.FromStream(backStream);

        for (int i = 0; i < cardCountWidth * cardCountHeight; i++)
        {
            if (i == backImageIndex)
                continue;
            var x = i % cardCountWidth;
            var y = i / cardCountWidth;

            using var image = new Bitmap(cardWidth, cardHeight);

            using (var gfx = Graphics.FromImage(image))
                gfx.DrawImage(src, new Rectangle(0, 0, cardWidth, cardHeight), new Rectangle(x * cardWidth, y * cardHeight, cardWidth, cardHeight), GraphicsUnit.Pixel);

            using var imageStream = new MemoryStream();
            image.Save(imageStream, System.Drawing.Imaging.ImageFormat.Png);

            using var xImage = XImage.FromStream(imageStream);

            yield return (xImage, xBack);
        }
    }

}

public class ZipParameter : FileParameter, IConsumer<ZipParameter>
{
    public static new string Argument => "Zip";


    private ZipParameter(Func<IEnumerable<(XImage front, XImage back)>> images) : base(images)
    {
    }


    public static new ZipParameter Consume(ref ReadOnlySpan<string> input)
    {

        if (!Consume<ImageFormat>(ref input, out var format))
            throw new ArgumentException("Image format expected as secondparameter");


        if (!Consume<string>(ref input, out var path))
            throw new ArgumentException("Path to zip Expected as first parameter");




        switch (format)
        {
            case ImageFormat.Pdf:
                if (!Consume<int>(ref input, out var backIndex))
                    backIndex = 0;

                return new ZipParameter(() =>
                {
                    var tempPath = System.IO.Path.GetTempFileName();
                    File.Delete(tempPath);
                    var tempDirectory = Directory.CreateDirectory(tempPath);
                    using (Stream stream = File.OpenRead(path))
                    using (var reader = ReaderFactory.Open(stream))
                    {
                        while (reader.MoveToNextEntry())
                        {
                            if (!reader.Entry.IsDirectory)
                            {
                                Console.WriteLine(reader.Entry.Key);
                                reader.WriteEntryToDirectory(tempDirectory.FullName, new ExtractionOptions()
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                        }
                    }


                    return tempDirectory.GetFiles().Where(x => x.Extension == ".pdf").Select(pdf => GetCardsFromPdf(pdf.FullName, backIndex)).SelectMany(x => x);
                });
            case ImageFormat.Images:

                if (!Consume<string>(ref input, out var backSearchPattern))
                    throw new ArgumentException("Image back name pattern expected");


                return new ZipParameter(() =>
                {
                    var tempPath = System.IO.Path.GetTempFileName();
                    File.Delete(tempPath);
                    var tempDirectory = Directory.CreateDirectory(tempPath);
                    using (Stream stream = File.OpenRead(path))
                    using (var reader = ReaderFactory.Open(stream))
                    {
                        while (reader.MoveToNextEntry())
                        {
                            if (!reader.Entry.IsDirectory)
                            {
                                Console.WriteLine(reader.Entry.Key);
                                reader.WriteEntryToDirectory(tempDirectory.FullName, new ExtractionOptions()
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                        }
                    }

                    var files = tempDirectory.GetFiles();

                    var backReg = new Regex(backSearchPattern);
                    var possibleBacks = files.Where(x => backReg.IsMatch(x.Name)).ToArray();

                    var backPath = possibleBacks.Length switch
                    {
                        0 => throw new IOException("back of card not found"),
                        1 => possibleBacks[0].FullName,
                        _ => throw new IOException("More then one image matched card"),
                    };

                    var rest = files.Where(x => x.FullName != backPath).Select(x => x.FullName).ToArray();

                    return GetCardsFromFiles(Enumerable.Repeat(backPath, 1).Concat(rest).ToArray());
                });


            case ImageFormat.MultiImages:
                if (!Consume<int>(ref input, out var imagePerRow))
                    throw new ArgumentException("Image back name pattern expected");

                if (!Consume<int>(ref input, out var imagePerColumn))
                    throw new ArgumentException("Image back name pattern expected");

                if (!Consume<int>(ref input, out backIndex))
                    backIndex = 0;

                return new ZipParameter(() =>
                {
                    var tempPath = System.IO.Path.GetTempFileName();
                    File.Delete(tempPath);
                    var tempDirectory = Directory.CreateDirectory(tempPath);
                    using (Stream stream = File.OpenRead(path))
                    using (var reader = ReaderFactory.Open(stream))
                    {
                        while (reader.MoveToNextEntry())
                        {
                            if (!reader.Entry.IsDirectory)
                            {
                                Console.WriteLine(reader.Entry.Key);
                                reader.WriteEntryToDirectory(tempDirectory.FullName, new ExtractionOptions()
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                        }
                    }


                    return tempDirectory.GetFiles().Select(file => GetCardsFromImage(file.FullName, imagePerRow, imagePerColumn, backIndex)).SelectMany(x => x);
                });
            default:
                throw new ArgumentException("Unknowin ImageFormat");

        }



    }
}


public class DirectoryParameter : FileParameter, IConsumer<DirectoryParameter>
{
    public static new string Argument => "Dir";


    private DirectoryParameter(Func<IEnumerable<(XImage front, XImage back)>> images) : base(images)
    {
    }


    public static new DirectoryParameter Consume(ref ReadOnlySpan<string> input)
    {


        if (!Consume<ImageFormat>(ref input, out var format))
            throw new ArgumentException("Image format expected as secondparameter");

        if (!Consume<string>(ref input, out var path))
            throw new ArgumentException("Path to zip Expected as first parameter");



        var dir = new DirectoryInfo(path);

        switch (format)
        {
            case ImageFormat.Pdf:
                if (!Consume<int>(ref input, out var backIndex))
                    backIndex = 0;

                return new DirectoryParameter(() => dir.GetFiles().Where(x => x.Extension == ".pdf").Select(pdf => GetCardsFromPdf(pdf.FullName, backIndex)).SelectMany(x => x));
            case ImageFormat.Images:

                if (!Consume<string>(ref input, out var backSearchPattern))
                    throw new ArgumentException("Image back name pattern expected");


                return new DirectoryParameter(() =>
                {
                    var files = dir.GetFiles();

                    var backReg = new Regex(backSearchPattern);
                    var possibleBacks = files.Where(x => backReg.IsMatch(x.Name)).ToArray();

                    var backPath = possibleBacks.Length switch
                    {
                        0 => throw new IOException("back of card not found"),
                        1 => possibleBacks[0].FullName,
                        _ => throw new IOException("More then one image matched card"),
                    };

                    var rest = files.Where(x => x.FullName != backPath).Select(x => x.FullName).ToArray();

                    return GetCardsFromFiles(Enumerable.Repeat(backPath, 1).Concat(rest).ToArray());
                });


            case ImageFormat.MultiImages:
                if (!Consume<int>(ref input, out var imagePerRow))
                    throw new ArgumentException("Image back name pattern expected");

                if (!Consume<int>(ref input, out var imagePerColumn))
                    throw new ArgumentException("Image back name pattern expected");

                if (!Consume<int>(ref input, out backIndex))
                    backIndex = 0;

                return new DirectoryParameter(() =>
                {


                    return dir.GetFiles().Select(file => GetCardsFromImage(file.FullName, imagePerRow, imagePerColumn, backIndex)).SelectMany(x => x);
                });
            default:
                throw new ArgumentException("Unknowin ImageFormat");

        }



    }
}

public class SizeParameter : ConsumerBase, IConsumer<SizeParameter>
{
    public int Width { get; init; }
    public int Height { get; init; }

    public static string Argument => "Size";

    public static SizeParameter Consume(ref ReadOnlySpan<string> input)
    {
        if (Consume<int>(ref input, out var width) && Consume<int>(ref input, out var height))
        {
            return new SizeParameter() { Width = width, Height = height };

        }
        throw new ArgumentException("Two intereger expected");


    }
}
public class GapParameter : ConsumerBase, IConsumer<GapParameter>
{
    public int Width { get; init; }

    public static string Argument => "Gap";

    public static GapParameter Consume(ref ReadOnlySpan<string> input)
    {
        if (Consume<int>(ref input, out var width))
        {
            return new GapParameter() { Width = width };
        }
        throw new ArgumentException("One intereger expected");


    }
}
public class NoBackParameter : ConsumerBase, IConsumer<NoBackParameter>
{
    public bool Print { get; init; }

    public static string Argument => "no-Back";

    public static NoBackParameter Consume(ref ReadOnlySpan<string> input)
    {
        return new NoBackParameter() { Print = false };

    }
}
public class NoFrontParameter : ConsumerBase, IConsumer<NoFrontParameter>
{
    public bool Print { get; init; }

    public static string Argument => "no-Front";

    public static NoFrontParameter Consume(ref ReadOnlySpan<string> input)
    {
        return new NoFrontParameter() { Print = false };

    }
}


public class LandscapeParameter : ConsumerBase, IConsumer<LandscapeParameter>
{
    public PdfSharp.PageOrientation Orientation { get; init; }

    public static string Argument => "Landscape";

    public static LandscapeParameter Consume(ref ReadOnlySpan<string> input)
    {
        return new LandscapeParameter() { Orientation = PdfSharp.PageOrientation.Landscape };

    }
}


public enum Bleed
{
    None,
    Front = 1,
    Back = 2,
}

public class BleedParameter : ConsumerBase, IConsumer<BleedParameter>
{
    public Bleed Bleed { get; init; }

    public static string Argument => "bleed";

    public static BleedParameter Consume(ref ReadOnlySpan<string> input)
    {
        if (Consume<Bleed>(ref input, out var bleed))
        {
            return new BleedParameter() { Bleed = bleed };
        }
        throw new ArgumentException("Front, Back expected for Bleed");



    }
}
