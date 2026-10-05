using System;
using System.Collections.Generic;
using McsMCP.Server;
using Newtonsoft.Json.Linq;

namespace McsMCP.Tools
{
    /// <summary>
    /// Get general game information
    /// </summary>
    public class GetGameInfoToolDefinition : ToolDefinitionBase
    {
        public override string Name => "get_game_info";
        public override string Description => @"Get information about the running game including:
- Game name, developer, version
- Unity version
- Platform information
- BepInEx version
- Current scene information
- Screen resolution";

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>()
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            try
            {
                var json = UnityHelper.GetGameInfoJson();
                return TextResult(json);
            }
            catch (Exception ex)
            {
                return ErrorResult($"Failed to get game info: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Get time information
    /// </summary>
    public class GetTimeInfoToolDefinition : ToolDefinitionBase
    {
        public override string Name => "get_time_info";
        public override string Description => @"Get Unity time information including:
- Current time, delta time, fixed delta time
- Time scale (for slow-motion or pause detection)
- Frame count
- Real time since startup";

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>()
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            try
            {
                var timeInfo = UnityHelper.GetTimeInfo();
                return JsonResult(timeInfo);
            }
            catch (Exception ex)
            {
                return ErrorResult($"Failed to get time info: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Saves a screenshot of the game window to a file the caller names.
    ///
    /// WHY IT WRITES TO A FILE INSTEAD OF RETURNING THE IMAGE
    ///
    /// This tool used to return the PNG as an inline base64 image content block. Measured on a real
    /// 觅长生 session, one call produced 12,321,176 base64 characters = 9.2 MB of PNG, which is on
    /// the order of THREE MILLION tokens. That is larger than any model's context window, so the
    /// tool was not merely noisy - it was unusable: a single call would have blown the caller's
    /// context, and because it "succeeded" there was no error to recover from and no way to see the
    /// cost coming.
    ///
    /// Returning a path inverts that. The default result is a short JSON object; the image is only
    /// ever read back deliberately.
    ///
    /// WHY 'path' IS REQUIRED AND ABSOLUTE
    ///
    /// A default output directory would be guesswork: the mod cannot know whether the caller wants
    /// the image in a scratch folder, a project directory, or somewhere it can be opened by a
    /// human. An ABSOLUTE path is required because the server's working directory is the game's,
    /// which is not what a caller means by a relative path - so a relative one would silently
    /// resolve somewhere surprising.
    ///
    /// WHY CROP AND SCALE EXIST
    ///
    /// For a vision-capable caller the useful question is usually "what does THIS panel say",
    /// not "what does the whole 1920x1080 frame look like". Cropping to the region of interest and
    /// downscaling it produces a much smaller file that is easier to read, and coordinates can be
    /// taken from dump_menu_state or the transform tools. Both default to off, so the simple call
    /// still captures everything.
    /// </summary>
    public class TakeScreenshotToolDefinition : ToolDefinitionBase
    {
        public override string Name => "take_screenshot";

        public override bool RequiresMainThread => true;

        public override string Description => @"Save a PNG of the game window to a file and return a short JSON description of it. Never returned
inline - a full frame is ~9 MB (~3M tokens) and would exhaust your context. Read the file back
yourself.

path must be ABSOLUTE (the server's working directory is the game's). Cropping and downscaling are
the difference between a 9 MB file and a readable one; crop coordinates are SOURCE pixels with a
TOP-LEFT origin, matching other tools. Crop happens before scale.";

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["path"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "REQUIRED. Absolute path to write the PNG to. Parent directories "
                                    + "are created if missing. Any existing file at this path is "
                                    + "overwritten."
                    },
                    ["cropX"] = new ToolPropertySchema
                    {
                        Type = "number",
                        Description = "Left edge of the crop region, in source pixels. Default 0."
                    },
                    ["cropY"] = new ToolPropertySchema
                    {
                        Type = "number",
                        Description = "Top edge of the crop region, in source pixels (top-left origin). Default 0."
                    },
                    ["cropWidth"] = new ToolPropertySchema
                    {
                        Type = "number",
                        Description = "Width of the crop region, in source pixels. Default: to the right edge."
                    },
                    ["cropHeight"] = new ToolPropertySchema
                    {
                        Type = "number",
                        Description = "Height of the crop region, in source pixels. Default: to the bottom edge."
                    },
                    ["width"] = new ToolPropertySchema
                    {
                        Type = "number",
                        Description = "Downscale the result to this pixel width, preserving aspect "
                                    + "ratio. Ignored (not upscaled) if larger than the capture. "
                                    + "Example: 960."
                    }
                },
                Required = new List<string> { "path" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            var path = GetStringArg(arguments, "path");

            if (string.IsNullOrWhiteSpace(path))
            {
                return ErrorResult("'path' is required: pass an absolute path to write the PNG to.");
            }

            // Absolute-path check. Path.IsPathRooted is the .NET Framework-appropriate test
            // (Path.IsPathFullyQualified does not exist in net472). This rejects the common
            // mistake of passing "shot.png" or "./shots/x.png", which would otherwise resolve
            // against the GAME's working directory.
            if (!System.IO.Path.IsPathRooted(path))
            {
                return ErrorResult(
                    $"'path' must be absolute, got '{path}'. The server's working directory is the "
                    + "game's, so a relative path would not resolve where you expect.");
            }

            var cropX = GetIntArgNullable(arguments, "cropX");
            var cropY = GetIntArgNullable(arguments, "cropY");
            var cropWidth = GetIntArgNullable(arguments, "cropWidth");
            var cropHeight = GetIntArgNullable(arguments, "cropHeight");
            var targetWidth = GetIntArgNullable(arguments, "width");

            try
            {
                var shot = UnityHelper.CaptureScreenshot(
                    cropX, cropY, cropWidth, cropHeight, targetWidth, out var captureError);

                if (shot == null)
                {
                    return ErrorResult($"Failed to capture screenshot: {captureError}");
                }

                // Create the parent directory rather than letting WriteAllBytes throw
                // DirectoryNotFoundException. A caller that passes a fresh path should get a file,
                // not a lecture about mkdir.
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                    }

                    System.IO.File.WriteAllBytes(path, shot.Png);
                }
                catch (Exception ioEx)
                {
                    // ★ Deliberately a hard error, not a logged warning.
                    //
                    // The old implementation logged a warning and still returned the image, so a
                    // caller whose path was unwritable got a "successful" result and no file. That
                    // is the worst possible outcome for a tool whose entire job is writing a file.
                    return ErrorResult(
                        $"Screenshot captured ({shot.OutputWidth}x{shot.OutputHeight}, "
                        + $"{shot.Png.Length} bytes) but could not be written to '{path}': {ioEx.Message}");
                }

                McsMCPPlugin.Log?.LogInfo($"Screenshot saved to {path} ({shot.Png.Length} bytes)");

                return JsonResult(new
                {
                    path,
                    sourceWidth = shot.SourceWidth,
                    sourceHeight = shot.SourceHeight,
                    outputWidth = shot.OutputWidth,
                    outputHeight = shot.OutputHeight,
                    bytes = shot.Png.Length,
                    cropped = shot.Cropped,
                    scaled = shot.Scaled,
                    note = "Image written to disk and intentionally not returned inline. "
                         + (shot.Scaled
                             ? $"Downscaled from {shot.SourceWidth}x{shot.SourceHeight}."
                             : "Full captured resolution.")
                });
            }
            catch (Exception ex)
            {
                return ErrorResult($"Failed to take screenshot: {ex.Message}");
            }
        }
    }
}