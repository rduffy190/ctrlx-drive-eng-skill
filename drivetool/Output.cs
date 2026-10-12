using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DriveTool
{
    /// <summary>
    /// Results go to stdout as snake_case JSON so they can be piped/parsed; progress and log lines go to stderr.
    /// </summary>
    public static class Output
    {
        public const int ExitOk = 0;
        public const int ExitError = 1;
        public const int ExitUsage = 2;
        /// <summary>A write was planned but not executed because --yes was not given.</summary>
        public const int ExitConfirmationRequired = 3;

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = null,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        public static void Json(object value)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        }

        public static void Log(string message)
        {
            Console.Error.WriteLine(message);
        }

        /// <summary>Prints the plan and returns exit code 3; callers use it when --yes is missing.</summary>
        public static int ConfirmationRequired(object plan)
        {
            Json(new { confirmation_required = true, plan });
            Log("Nothing was written. Re-run with --yes to execute this plan.");
            return ExitConfirmationRequired;
        }
    }
}
