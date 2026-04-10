using Microsoft.EntityFrameworkCore;
using SQLInventory.Data;
using SQLInventory.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.CommandTimeout(60)));

builder.Services.AddScoped<InstanceService>();
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddScoped<AvailabilityGroupService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<DiscoveryService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<SQLInventory.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
