using MotorShop.Fitment.Domain;

namespace MotorShop.Fitment.Services;

/// <summary>
/// Starter list of common motorcycles, imported on module installation.
/// Year ranges are approximate and meant to be reviewed by the merchant.
/// </summary>
internal static class VehicleSeedData
{
    public static IEnumerable<MotorVehicle> GetVehicles()
    {
        return
        [
            V("Honda", "CBR 250R", 2011, 2013, 250),
            V("Honda", "CBR 650R", 2019, 2025, 649),
            V("Honda", "CB 125F", 2015, 2025, 124),
            V("Honda", "PCX 125", 2010, 2025, 125),
            V("Honda", "Activa 125", 2014, 2025, 124),
            V("Honda", "Dio 110", 2019, 2025, 110),
            V("Honda", "Forza 250", 2018, 2025, 249),
            V("Honda", "NC 750X", 2014, 2025, 745),
            V("Honda", "CRF1100L Africa Twin", 2020, 2025, 1084),

            V("Yamaha", "YZF-R25", 2014, 2025, 249),
            V("Yamaha", "MT-25", 2015, 2025, 249),
            V("Yamaha", "MT-07", 2014, 2025, 689),
            V("Yamaha", "NMAX 125", 2015, 2025, 125),
            V("Yamaha", "XMAX 250", 2014, 2025, 249),
            V("Yamaha", "Tracer 7", 2021, 2025, 689),

            V("Kawasaki", "Ninja 250R", 2008, 2012, 249),
            V("Kawasaki", "Ninja 400", 2018, 2025, 399),
            V("Kawasaki", "Z650", 2017, 2025, 649),
            V("Kawasaki", "Z900", 2017, 2025, 948),
            V("Kawasaki", "Versys 650", 2015, 2025, 649),

            V("Suzuki", "GSX-R125", 2017, 2025, 124),
            V("Suzuki", "Address 110", 2015, 2025, 113),
            V("Suzuki", "Burgman 400", 2017, 2025, 400),
            V("Suzuki", "V-Strom 650", 2012, 2025, 645),

            V("KTM", "125 Duke", 2011, 2025, 125),
            V("KTM", "250 Duke", 2015, 2025, 249),
            V("KTM", "390 Duke", 2013, 2025, 373),
            V("KTM", "RC 390", 2014, 2025, 373),
            V("KTM", "390 Adventure", 2020, 2025, 373),

            V("Bajaj", "Pulsar NS200", 2012, 2025, 199),
            V("Bajaj", "Pulsar RS200", 2015, 2025, 199),
            V("Bajaj", "Dominar 400", 2017, 2025, 373),

            V("TVS", "Apache RTR 200 4V", 2016, 2025, 197),
            V("TVS", "Jupiter 110", 2013, 2025, 110),
            V("TVS", "Ntorq 125", 2018, 2025, 124)
        ];
    }

    private static MotorVehicle V(string make, string model, int yearFrom, int yearTo, int? cc)
        => new() { Make = make, Model = model, YearFrom = yearFrom, YearTo = yearTo, EngineCc = cc };
}
