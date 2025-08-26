namespace FireIncidents.Logging
{
    public static class LoggingExtensions
    {
        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, IConfiguration configuration)
        {
            builder.Services.AddOptions<FileLoggerOptions>()
                .Bind(configuration.GetSection("Logging:File"));

            builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();

            return builder;
        }
    }
}