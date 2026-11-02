namespace Hydra.Analyzers.Sample;

public enum GuestErrors { NotFound, AlreadyRegistered }

public sealed class Guest
{
    public string Name { get; set; } = "";
}

public sealed class Examples
{
    private static Result<Guest> GetGuest(bool exists) =>
        exists ? Result.Success(new Guest { Name = "Ada" }) : AppError.NotFound(GuestErrors.NotFound);

    public Result<Guest> Guard(bool exists)
    {
        var (guest, error) = GetGuest(exists);
        if (error is not null)
        {
            return error;
        }

        return Result.Success(guest);
    }

    public Result<Guest> Switch(bool exists)
    {
        var (guest, error) = GetGuest(exists);
        if (error is not null)
        {
            switch (error.Code)
            {
                case GuestErrors.NotFound:
                    return AppError.NotFound(GuestErrors.NotFound);
                default:
                    return error;
            }
        }
        return Result.Success(guest);
    }

    public Result<Guest> Recover(bool exists)
    {
        var (guest, error) = GetGuest(exists);
        if (error is not null)
        {
            if (error.Code is not GuestErrors.NotFound)
            {
                return error;
            }

            guest = new Guest { Name = "Fallback" };
        }
        return Result.Success(guest);
    }

#if HYDRA_DIAGNOSTIC_DEMO
    // Build with -p:DefineConstants=HYDRA_DIAGNOSTIC_DEMO to see RES002.
    public Result<Guest> Broken(bool exists)
    {
        var (guest, error) = GetGuest(exists);
        return Result.Success(guest);
    }
#endif
}
