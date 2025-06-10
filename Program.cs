using BackendAnticipos.Services;
using BackendAnticipos.Models.Settings;
using BackendAnticipos.Services.Auth;
using Serilog;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});

// 📌 Crear directorio de logs si no existe
var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

// 📌 Configuración de Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddHttpContextAccessor();

// 📌 Registrar servicios
builder.Services.AddControllers();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<InformixService>();

builder.Services.Configure<SmtpSettings>(
    builder.Configuration.GetSection("Smtp"));

// 📌 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuraci�n de CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins("http://192.168.20.30:8089", "https://portalpagos.recamier.com", "http://localhost:3000") // Cambia por tus dominios permitidos
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Middleware de Swagger
app.UseSwagger();
if (app.Environment.IsProduction())
{
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WompiRecamier API v1");
        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseSwaggerUI();
}

// Middleware de CORS
if (app.Environment.IsProduction())
{
    app.UseCors("AllowSpecificOrigins");
}
else
{
    app.UseCors("AllowAllOrigins"); // En desarrollo, permitir cualquier origen
}

// Middleware para servir el frontend
app.UseDefaultFiles(); // Redirige autom�ticamente a index.html
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        Console.WriteLine($"Sirviendo archivo est�tico: {ctx.File.PhysicalPath}");
    }
});

app.UseAuthorization();
app.MapControllers();

// 📌 React frontend
app.MapFallbackToFile("index.html");

app.Run();
