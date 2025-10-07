using BackendAnticipos.Services;
using BackendAnticipos.Models.Settings;
using BackendAnticipos.Services.Auth;
using Microsoft.Extensions.FileProviders;
using Serilog;
using System.IO;

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

// Configuración de CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins(
            "http://192.168.20.30:8089",
            "http://anticiposproveedores.recamier.com:8083",
            "http://localhost:3000"
        )
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

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        Console.WriteLine($"Sirviendo archivo estático: {ctx.File.PhysicalPath}");
    }
});

// 📌 Crear directorio "Soportes" si no existe antes de exponer archivos estáticos
var soportesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Soportes");
if (!Directory.Exists(soportesDirectory))
{
    Directory.CreateDirectory(soportesDirectory);
}

// Servir archivos estáticos desde /soportes
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(soportesDirectory),
    RequestPath = "/soportes"
});

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
    app.UseCors("AllowAllOrigins");
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "0");

        Console.WriteLine($"Sirviendo archivo estático: {ctx.File.PhysicalPath}");
    }
});

app.UseAuthorization();
app.MapControllers();

app.MapFallbackToFile("index.html");

app.Run();
