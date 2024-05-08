using CreekRiver.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using CreekRiver.Models.DTOs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// allows passing datetimes without time zone data 
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// allows our api endpoints to access the database through Entity Framework Core
builder.Services.AddNpgsql<CreekRiverDbContext>(builder.Configuration["CreekRiverDbConnectionString"]);


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/api/campsites", (CreekRiverDbContext db) =>
{
    return db.Campsites
    .Select(c => new CampsiteDTO
    {
        Id = c.Id,
        Nickname = c.Nickname,
        ImageUrl = c.ImageUrl,
        CampsiteTypeId = c.CampsiteTypeId
    }).ToList();
});

app.MapGet("/api/campsites/{id}", (CreekRiverDbContext db, int id) =>
{
    return db.Campsites
        .Include(c => c.CampsiteType)
        .Select(c => new CampsiteDTO
        {
            Id = c.Id,
            Nickname = c.Nickname,
            CampsiteTypeId = c.CampsiteTypeId,
            CampsiteType = new CampsiteTypeDTO
            {
                Id = c.CampsiteType.Id,
                CampsiteTypeName = c.CampsiteType.CampsiteTypeName,
                FeePerNight = c.CampsiteType.FeePerNight,
                MaxReservationDays = c.CampsiteType.MaxReservationDays
            }
        })
        .Single(c => c.Id == id);
});

app.MapPost("/api/campsites", (CreekRiverDbContext db, Campsite campsite) =>
{
    db.Campsites.Add(campsite);
    db.SaveChanges();
    return Results.Created($"/api/campsites/{campsite.Id}", campsite);
});


app.MapPut("/api/campsites/{id}", (CreekRiverDbContext db, int id, Campsite campsite) =>
{
    Campsite campsiteToUpdate = db.Campsites.SingleOrDefault(campsite => campsite.Id == id);
    if (campsiteToUpdate == null)
    {
        return Results.NotFound();
    }
    campsiteToUpdate.Nickname = campsite.Nickname;
    campsiteToUpdate.CampsiteTypeId = campsite.CampsiteTypeId;
    campsiteToUpdate.ImageUrl = campsite.ImageUrl;

    db.SaveChanges();
    return Results.NoContent();
});

app.MapDelete("/api/campsites/{id}", (CreekRiverDbContext db, int id) =>
{
    Campsite campsite = db.Campsites.SingleOrDefault(campsite => campsite.Id == id);
    if (campsite == null)
    {
        return Results.NotFound();
    }
    db.Campsites.Remove(campsite);
    db.SaveChanges();
    return Results.NoContent();

});

app.MapGet("/api/reservations", (CreekRiverDbContext db) =>
{
    return db.Reservations
        .Include(r => r.UserProfile)
        .Include(r => r.Campsite)
        .ThenInclude(c => c.CampsiteType)
        .OrderBy(res => res.CheckinDate)
        .Select(r => new ReservationDTO
        {
            Id = r.Id,
            CampsiteId = r.CampsiteId,
            UserProfileId = r.UserProfileId,
            CheckinDate = r.CheckinDate,
            CheckoutDate = r.CheckoutDate,
            UserProfile = new UserProfileDTO
            {
                Id = r.UserProfile.Id,
                FirstName = r.UserProfile.FirstName,
                LastName = r.UserProfile.LastName,
                Email = r.UserProfile.Email
            },
            Campsite = new CampsiteDTO
            {
                Id = r.Campsite.Id,
                Nickname = r.Campsite.Nickname,
                ImageUrl = r.Campsite.ImageUrl,
                CampsiteTypeId = r.Campsite.CampsiteTypeId,
                CampsiteType = new CampsiteTypeDTO
                {
                    Id = r.Campsite.CampsiteType.Id,
                    CampsiteTypeName = r.Campsite.CampsiteType.CampsiteTypeName,
                    MaxReservationDays = r.Campsite.CampsiteType.MaxReservationDays,
                    FeePerNight = r.Campsite.CampsiteType.FeePerNight
                }
            }
        })
        .ToList();
});

app.MapPost("/api/reservations", async (CreekRiverDbContext db, Reservation newRes) =>
{
    try{
    if (newRes.CheckoutDate <= newRes.CheckinDate)
    {
        return Results.BadRequest("Reservation checkout must be at least one day after checkin");
    }

// Add logic to the handler to check that the reservation does not conflict with another reservation for that campsite that already exists (a new Checkin is allowed to happen on the same day as an existing Checkout).

    bool hasConflict =await db.Reservations
        .AnyAsync(r =>
            r.CampsiteId == newRes.CampsiteId &&
            ((newRes.CheckinDate >= r.CheckinDate && newRes.CheckinDate < r.CheckoutDate) ||
             (newRes.CheckoutDate > r.CheckinDate && newRes.CheckoutDate <= r.CheckoutDate)));

//SELECT COUNT(*) AS ConflictCount
// FROM Reservations AS r
// WHERE r.CampsiteId = @CampsiteId
// AND ((@CheckinDate >= r.CheckinDate AND @CheckinDate < r.CheckoutDate) OR
//      (@CheckoutDate > r.CheckinDate AND @CheckoutDate <= r.CheckoutDate))
    if (hasConflict)
    {
        // Return conflict response
        return Results.Conflict("Reservation conflicts with existing reservation.");
    }
    // A reservation cannot be made for a same-day checkin or (more obviously) a check-in in the past. Prevent this with logic in the handler
    if(newRes.CheckinDate==DateTime.Today)
    {
        return Results.BadRequest("Same day Checkin is Not Allowed");
    }

    // Open-ended reservations are not allowed. Make sure that the reservation has both a CheckinDate and a CheckoutDate
    if(newRes.CheckinDate==null||newRes.CheckoutDate==null)
    {
        return Results.BadRequest("Either Checkin or Checkout dates are missing");
    }

    // check if reservation is too long
    Campsite campsite = db.Campsites.Include(c => c.CampsiteType).Single(c => c.Id == newRes.CampsiteId);
    if (campsite != null && newRes.TotalNights > campsite.CampsiteType.MaxReservationDays)
    {
        return Results.BadRequest("Reservation exceeds maximum reservation days for this campsite type");
    }
    db.Reservations.Add(newRes);
    
    db.SaveChanges();
    return Results.Created($"/api/reservations/{newRes.Id}", newRes);
    }
    catch(DbUpdateException)
    {
        return Results.BadRequest("Invalid data submitted");
    }
});

app.MapDelete("/api/reservations",(CreekRiverDbContext db,int id)=>{

    Reservation reservation=db.Reservations.Single(r=>r.Id==id);
    if(reservation==null)
    {
        return Results.NotFound();
    }
    db.Reservations.Remove(reservation);
    db.SaveChanges();
    return Results.NoContent();
} );

app.Run();

