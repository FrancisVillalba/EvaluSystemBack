using EvaluSystemBack.Models;
using System.Text;
using System.Text.Json.Serialization;
using EvaluSystemBack.Data;
using EvaluSystemBack.Middleware;
using EvaluSystemBack.Options;
using EvaluSystemBack.Repositories;
using EvaluSystemBack.Repositories.Interfaces;
using EvaluSystemBack.Security;
using EvaluSystemBack.Services;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
const long maxUploadBytes = 500L * 1024L * 1024L;

// Add services to the container.

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<EvaluSystemDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped(typeof(IGenericService<>), typeof(GenericService<>));
builder.Services.AddScoped<IConfiguracionService, ConfiguracionService>();
builder.Services.AddScoped<IPermisoService, PermisoService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IVentaImpresionService, VentaImpresionService>();
builder.Services.AddScoped<IEstadoVentaFlujoService, EstadoVentaFlujoService>();
builder.Services.AddScoped<IPedidoFlujoService, PedidoFlujoService>();
builder.Services.AddScoped<INotificacionService, NotificacionService>();
builder.Services.AddScoped<PermissionAuthorizationFilter>();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = maxUploadBytes;
});

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("La configuracion Jwt es obligatoria.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.FindFirst("token_type")?.Value != "access")
                {
                    context.Fail("Solo se permiten tokens de acceso.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "http://localhost:4200",
                "http://localhost:5173",
                "https://localhost:3000",
                "https://localhost:4200",
                "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers()
    .AddMvcOptions(options =>
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.Filters.Add(new AuthorizeFilter(policy));
        options.Filters.Add<PermissionAuthorizationFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Ingrese el token JWT. Ejemplo: Bearer eyJhbGciOi..."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EvaluSystemDbContext>();
    const string moduleName = "BuscadorGeneral";
    var form = await db.Formularios.FirstOrDefaultAsync(x => x.Nombre == moduleName || x.Nombre == "Buscador general");
    if (form is null)
    {
        form = new Formulario
        {
            Nombre = moduleName,
            Descripcion = "Consulta general de ventas",
            Ruta = "/buscador-general",
            Icono = "search",
            Orden = 25,
            Estado = true
        };
        db.Formularios.Add(form);
        await db.SaveChangesAsync();
    }

    else
    {
        form.Nombre = moduleName;
        form.Descripcion = "Consulta general de ventas";
        form.Ruta = "/buscador-general";
        form.Icono = "search";
        form.Orden = 25;
        form.Estado = true;
        await db.SaveChangesAsync();
    }

    var controlProfileId = await db.Perfiles
        .Where(x => x.Estado && x.Nombre == "Control")
        .Select(x => x.Id)
        .FirstOrDefaultAsync();
    if (controlProfileId > 0)
    {
        var permission = await db.PerfilFormularioPermisos
            .FirstOrDefaultAsync(x => x.PerfilId == controlProfileId && x.FormularioId == form.Id);
        if (permission is null)
        {
            db.PerfilFormularioPermisos.Add(new PerfilFormularioPermiso
            {
                PerfilId = controlProfileId,
                FormularioId = form.Id,
                PuedeVer = true,
                PuedeCrear = false,
                PuedeEditar = false,
                PuedeEliminar = false
            });
        }
        else
        {
            permission.PuedeVer = true;
            permission.PuedeCrear = false;
            permission.PuedeEditar = false;
            permission.PuedeEliminar = false;
        }
        await db.SaveChangesAsync();
    }
}


app.UseMiddleware<ErrorHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
