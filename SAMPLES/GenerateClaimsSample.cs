#:package ClosedXML@0.105.1
#:property RestorePackagesWithLockFile=false
#:property PublishAot=false

// Generates SAMPLES/ClaimsSample2025.xlsx: 10,000 contrived medical claim lines for 500 contrived members, all with
// service dates in calendar year 2025. Every person, address and identifier is invented:
//   - SSNs start 900-999, a range the Social Security Administration never issues;
//   - phone numbers are 555-0100 to 555-0199, the range reserved for fiction;
//   - insurance IDs, group numbers, claim IDs and provider IDs are random, prefixed so they can't pass for real ones.
// The codes (HCPCS/CPT, ICD-10-CM, place of service) are real code values, so the data looks like claims; the short
// descriptions are written for this file. Run from the repository root:  dotnet run SAMPLES/GenerateClaimsSample.cs
// The random seed is fixed, so it makes the same workbook each time.

using ClosedXML.Excel;

const int MemberCount = 500;
const int LineCount = 10_000;
var rng = new Random(2025);
var output = Path.Combine(args.Length > 0 ? args[0] : "SAMPLES", "ClaimsSample2025.xlsx");

T Pick<T>(IReadOnlyList<T> items) => items[rng.Next(items.Count)];
decimal Money(double value) => Math.Round((decimal)value, 2);

// ── Reference data ───────────────────────────────────────────────────

string[] femaleNames = ["Mary", "Patricia", "Jennifer", "Linda", "Elizabeth", "Barbara", "Susan", "Jessica", "Sarah", "Karen",
    "Lisa", "Nancy", "Betty", "Sandra", "Margaret", "Ashley", "Kimberly", "Emily", "Donna", "Michelle", "Carol", "Amanda",
    "Melissa", "Deborah", "Stephanie", "Rebecca", "Sharon", "Laura", "Cynthia", "Dorothy", "Amy", "Kathleen", "Angela",
    "Shirley", "Emma", "Brenda", "Pamela", "Nicole", "Anna", "Samantha", "Katherine", "Christine", "Debra", "Rachel",
    "Carolyn", "Janet", "Maria", "Olivia", "Heather", "Helen", "Catherine", "Diane", "Julie", "Victoria", "Joyce", "Lauren",
    "Kelly", "Christina", "Ruth", "Joan", "Virginia", "Judith", "Evelyn", "Hannah", "Andrea", "Megan", "Cheryl", "Jacqueline",
    "Madison", "Teresa", "Abigail", "Sophia", "Martha", "Sara", "Gloria", "Janice", "Kathryn", "Ann", "Isabella", "Judy",
    "Charlotte", "Julia", "Grace", "Amber", "Alice", "Jean", "Denise", "Frances", "Danielle", "Marilyn", "Natalie", "Beverly",
    "Diana", "Brittany", "Theresa", "Kayla", "Alexis", "Doris", "Lori", "Tiffany", "Priya", "Mei", "Fatima", "Aaliyah",
    "Ximena", "Guadalupe", "Yesenia", "Keisha", "Ngozi", "Anika", "Siobhan", "Ingrid", "Rosa", "Lucia", "Hana", "Leilani"];
string[] maleNames = ["James", "Robert", "John", "Michael", "David", "William", "Richard", "Joseph", "Thomas", "Christopher",
    "Charles", "Daniel", "Matthew", "Anthony", "Mark", "Donald", "Steven", "Andrew", "Paul", "Joshua", "Kenneth", "Kevin",
    "Brian", "George", "Timothy", "Ronald", "Jason", "Edward", "Jeffrey", "Ryan", "Jacob", "Gary", "Nicholas", "Eric",
    "Jonathan", "Stephen", "Larry", "Justin", "Scott", "Brandon", "Benjamin", "Samuel", "Gregory", "Alexander", "Patrick",
    "Frank", "Raymond", "Jack", "Dennis", "Jerry", "Tyler", "Aaron", "Jose", "Adam", "Nathan", "Henry", "Zachary", "Douglas",
    "Peter", "Kyle", "Noah", "Ethan", "Jeremy", "Walter", "Christian", "Keith", "Roger", "Terry", "Austin", "Sean", "Gerald",
    "Carl", "Harold", "Dylan", "Arthur", "Lawrence", "Jordan", "Jesse", "Bryan", "Billy", "Bruce", "Gabriel", "Joe", "Logan",
    "Alan", "Juan", "Albert", "Willie", "Elijah", "Wayne", "Randy", "Vincent", "Mason", "Roy", "Ralph", "Bobby", "Russell",
    "Bradley", "Philip", "Eugene", "Rajesh", "Wei", "Omar", "DeShawn", "Mateo", "Santiago", "Kwame", "Hiroshi", "Liam",
    "Declan", "Lars", "Dmitri", "Tariq", "Kai", "Malik", "Andre"];
string[] lastNames = ["Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez",
    "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson", "Thomas", "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez",
    "Thompson", "White", "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson", "Walker", "Young", "Allen", "King",
    "Wright", "Scott", "Torres", "Nguyen", "Hill", "Flores", "Green", "Adams", "Nelson", "Baker", "Hall", "Rivera", "Campbell",
    "Mitchell", "Carter", "Roberts", "Gomez", "Phillips", "Evans", "Turner", "Diaz", "Parker", "Cruz", "Edwards", "Collins",
    "Reyes", "Stewart", "Morris", "Morales", "Murphy", "Cook", "Rogers", "Gutierrez", "Ortiz", "Morgan", "Cooper", "Peterson",
    "Bailey", "Reed", "Kelly", "Howard", "Ramos", "Kim", "Cox", "Ward", "Richardson", "Watson", "Brooks", "Chavez", "Wood",
    "James", "Bennett", "Gray", "Mendoza", "Ruiz", "Hughes", "Price", "Alvarez", "Castillo", "Sanders", "Patel", "Myers",
    "Long", "Ross", "Foster", "Jimenez", "Raghunathan", "Kowalski", "O'Neil", "McAllister", "Okafor", "Nakamura", "Schmidt",
    "Rossi", "Novak", "Haddad", "Chen", "Wang", "Singh", "Kaur", "Fitzgerald", "Van der Berg", "St. Clair", "Delacroix",
    "Abernathy", "Lindqvist", "Papadopoulos", "Szymanski", "Oyelaran", "Bjornsson"];
string[] streetNames = ["Main", "Oak", "Pine", "Maple", "Cedar", "Elm", "Washington", "Lake", "Hill", "Walnut", "Park",
    "Sunset", "Lincoln", "Jackson", "Church", "Willow", "Highland", "Meadow", "River", "Spring", "Chestnut", "Franklin",
    "Ridge", "Valley", "Forest", "Hickory", "Birch", "Magnolia", "Dogwood", "Laurel", "Jefferson", "Madison", "Adams",
    "Prospect", "Center", "Mill", "Orchard", "Railroad", "Harbor", "Bayview", "Glenwood", "Fairview", "Woodland", "Cherry"];
string[] streetTypes = ["St", "Ave", "Rd", "Dr", "Ln", "Ct", "Blvd", "Way", "Pl", "Ter", "Cir"];
string[] units = ["Apt 2B", "Apt 101", "Unit 4", "Apt 3C", "Suite 210", "Apt 12", "#7", "Unit B"];
// Real city, state and ZIP combinations; the street addresses on them are invented.
(string City, string State, string Zip, string Area)[] places =
[
    ("Boston", "MA", "02118", "617"), ("Worcester", "MA", "01608", "508"), ("Springfield", "MA", "01103", "413"),
    ("Hartford", "CT", "06103", "860"), ("Providence", "RI", "02903", "401"), ("Albany", "NY", "12207", "518"),
    ("Buffalo", "NY", "14202", "716"), ("Rochester", "NY", "14604", "585"), ("Newark", "NJ", "07102", "973"),
    ("Philadelphia", "PA", "19107", "215"), ("Pittsburgh", "PA", "15222", "412"), ("Baltimore", "MD", "21201", "410"),
    ("Richmond", "VA", "23219", "804"), ("Raleigh", "NC", "27601", "919"), ("Charlotte", "NC", "28202", "704"),
    ("Columbia", "SC", "29201", "803"), ("Atlanta", "GA", "30303", "404"), ("Jacksonville", "FL", "32202", "904"),
    ("Tampa", "FL", "33602", "813"), ("Orlando", "FL", "32801", "407"), ("Nashville", "TN", "37203", "615"),
    ("Louisville", "KY", "40202", "502"), ("Columbus", "OH", "43215", "614"), ("Cleveland", "OH", "44114", "216"),
    ("Detroit", "MI", "48226", "313"), ("Indianapolis", "IN", "46204", "317"), ("Chicago", "IL", "60601", "312"),
    ("Milwaukee", "WI", "53202", "414"), ("Minneapolis", "MN", "55401", "612"), ("Des Moines", "IA", "50309", "515"),
    ("St. Louis", "MO", "63101", "314"), ("Kansas City", "MO", "64105", "816"), ("Omaha", "NE", "68102", "402"),
    ("Dallas", "TX", "75201", "214"), ("Houston", "TX", "77002", "713"), ("Austin", "TX", "78701", "512"),
    ("San Antonio", "TX", "78205", "210"), ("Oklahoma City", "OK", "73102", "405"), ("Denver", "CO", "80202", "303"),
    ("Albuquerque", "NM", "87102", "505"), ("Phoenix", "AZ", "85004", "602"), ("Salt Lake City", "UT", "84101", "801"),
    ("Las Vegas", "NV", "89101", "702"), ("Seattle", "WA", "98101", "206"), ("Portland", "OR", "97204", "503"),
    ("Sacramento", "CA", "95814", "916"), ("San Diego", "CA", "92101", "619"), ("Los Angeles", "CA", "90012", "213"),
];
(string Name, string Group)[] plans =
[
    ("Synthetic Health PPO", "GRP-SH100"), ("Synthetic Health HMO", "GRP-SH200"), ("Sample Mutual EPO", "GRP-SM300"),
    ("Sample Mutual HDHP", "GRP-SM350"), ("Contrived Care Medicare Advantage", "GRP-CC500"), ("Contrived Care Medicaid", "GRP-CC600"),
];

// Chronic conditions a member can have, and acute reasons for a visit (ICD-10-CM).
(string Code, string Description)[] chronic =
[
    ("E11.9", "Type 2 diabetes without complications"), ("I10", "Essential hypertension"), ("E78.5", "Hyperlipidemia"),
    ("J45.909", "Asthma, uncomplicated"), ("F41.1", "Generalized anxiety disorder"), ("F32.A", "Depression, unspecified"),
    ("K21.9", "Gastro-esophageal reflux"), ("M17.11", "Osteoarthritis, right knee"), ("G47.33", "Obstructive sleep apnea"),
    ("E03.9", "Hypothyroidism"), ("E66.9", "Obesity"), ("M54.50", "Low back pain"), ("N18.3", "Chronic kidney disease, stage 3"),
    ("I48.91", "Atrial fibrillation"), ("J44.9", "COPD"), ("E55.9", "Vitamin D deficiency"),
];
(string Code, string Description)[] acute =
[
    ("J06.9", "Acute upper respiratory infection"), ("J02.9", "Acute pharyngitis"), ("N39.0", "Urinary tract infection"),
    ("R51.9", "Headache"), ("R10.9", "Abdominal pain"), ("S93.401A", "Ankle sprain, initial"), ("H66.90", "Otitis media"),
    ("L03.90", "Cellulitis"), ("R05.9", "Cough"), ("J01.90", "Acute sinusitis"), ("M25.561", "Pain in right knee"),
    ("R07.9", "Chest pain"), ("A09", "Gastroenteritis"), ("U07.1", "COVID-19"), ("S61.411A", "Laceration of right hand"),
];
(string Code, string Description)[] preventive =
[
    ("Z00.00", "General adult exam"), ("Z00.129", "Routine child health exam"), ("Z23", "Encounter for immunization"),
    ("Z12.31", "Screening mammogram"), ("Z13.220", "Lipid screening"), ("Z12.11", "Colon cancer screening"),
];

// Services: code, description, typical charge, how units vary, the kind of visit, and the place of service.
var services = new (string Code, string Description, double Price, int MaxUnits, string Kind, string[] Places)[]
{
    ("99212", "Office visit, established, straightforward", 95, 1, "office", ["11", "02", "10"]),
    ("99213", "Office visit, established, low complexity", 135, 1, "office", ["11", "02", "10"]),
    ("99214", "Office visit, established, moderate complexity", 195, 1, "office", ["11", "02", "10"]),
    ("99215", "Office visit, established, high complexity", 265, 1, "office", ["11"]),
    ("99203", "Office visit, new patient, low complexity", 170, 1, "office", ["11"]),
    ("99204", "Office visit, new patient, moderate complexity", 255, 1, "office", ["11"]),
    ("99283", "Emergency department visit, moderate", 520, 1, "er", ["23"]),
    ("99284", "Emergency department visit, high", 890, 1, "er", ["23"]),
    ("99285", "Emergency department visit, highest", 1350, 1, "er", ["23"]),
    ("99221", "Initial hospital inpatient care", 310, 1, "inpatient", ["21"]),
    ("99232", "Subsequent hospital inpatient care", 160, 1, "inpatient", ["21"]),
    ("99238", "Hospital discharge day", 150, 1, "inpatient", ["21"]),
    ("99395", "Preventive visit, established, 18-39", 230, 1, "preventive", ["11"]),
    ("99396", "Preventive visit, established, 40-64", 250, 1, "preventive", ["11"]),
    ("99397", "Preventive visit, established, 65+", 265, 1, "preventive", ["11"]),
    ("99392", "Preventive visit, established, 1-4", 210, 1, "preventive", ["11"]),
    ("G0439", "Annual wellness visit, subsequent", 175, 1, "preventive", ["11"]),
    ("36415", "Routine venipuncture", 18, 1, "lab", ["81", "11"]),
    ("80053", "Comprehensive metabolic panel", 45, 1, "lab", ["81", "11"]),
    ("80061", "Lipid panel", 38, 1, "lab", ["81", "11"]),
    ("83036", "Hemoglobin A1C", 32, 1, "lab", ["81", "11"]),
    ("85025", "Complete blood count", 26, 1, "lab", ["81", "11"]),
    ("84443", "Thyroid stimulating hormone", 48, 1, "lab", ["81"]),
    ("81003", "Urinalysis, automated", 12, 1, "lab", ["81", "11", "20"]),
    ("87880", "Rapid strep test", 28, 1, "lab", ["11", "20"]),
    ("87635", "SARS-CoV-2 amplified probe", 95, 1, "lab", ["81", "20"]),
    ("71046", "Chest X-ray, 2 views", 120, 1, "imaging", ["22", "23", "11"]),
    ("73562", "Knee X-ray, 3 views", 115, 1, "imaging", ["22", "11"]),
    ("73610", "Ankle X-ray, 3 views", 105, 1, "imaging", ["22", "23", "20"]),
    ("77067", "Screening mammography, bilateral", 245, 1, "imaging", ["22", "11"]),
    ("74177", "CT abdomen and pelvis with contrast", 1250, 1, "imaging", ["22", "23"]),
    ("93000", "Electrocardiogram, complete", 55, 1, "office-test", ["11", "23"]),
    ("94010", "Spirometry", 65, 1, "office-test", ["11"]),
    ("95810", "Polysomnography, attended", 1450, 1, "office-test", ["19"]),
    ("45378", "Colonoscopy, diagnostic", 1650, 1, "procedure", ["24", "22"]),
    ("12002", "Simple repair of wound, 2.6-7.5 cm", 280, 1, "procedure", ["23", "20"]),
    ("20610", "Joint injection, major joint", 210, 1, "procedure", ["11"]),
    ("97110", "Therapeutic exercise, each 15 min", 48, 4, "therapy", ["11"]),
    ("97140", "Manual therapy, each 15 min", 45, 3, "therapy", ["11"]),
    ("97161", "Physical therapy evaluation, low", 135, 1, "therapy", ["11"]),
    ("90834", "Psychotherapy, 45 minutes", 140, 1, "behavioral", ["11", "02", "10"]),
    ("90837", "Psychotherapy, 60 minutes", 185, 1, "behavioral", ["11", "02", "10"]),
    ("90471", "Immunization administration", 28, 1, "vaccine", ["11", "01"]),
    ("90686", "Influenza vaccine, quadrivalent", 32, 1, "vaccine", ["11", "01"]),
    ("90715", "Tdap vaccine", 58, 1, "vaccine", ["11"]),
    ("J1100", "Injection, dexamethasone, 1 mg", 6, 10, "drug", ["11", "23"]),
    ("J3420", "Injection, vitamin B-12, up to 1000 mcg", 9, 1, "drug", ["11"]),
    ("J7613", "Albuterol inhalation solution, 1 mg", 4, 6, "drug", ["11", "23"]),
    ("A4253", "Blood glucose test strips, per 50", 42, 3, "dme", ["12"]),
    ("E0601", "CPAP device, monthly rental", 115, 1, "dme", ["12"]),
    ("E0114", "Crutches, underarm, pair", 65, 1, "dme", ["12", "23"]),
};
var placeNames = new Dictionary<string, string>
{
    ["01"] = "Pharmacy", ["02"] = "Telehealth (not in home)", ["10"] = "Telehealth (in home)", ["11"] = "Office",
    ["12"] = "Home", ["19"] = "Off-campus outpatient hospital", ["20"] = "Urgent care facility", ["21"] = "Inpatient hospital",
    ["22"] = "On-campus outpatient hospital", ["23"] = "Emergency room, hospital", ["24"] = "Ambulatory surgical center",
    ["81"] = "Independent laboratory",
};
// Which diagnoses fit which kinds of service.
var chronicCodes = chronic.Select(c => c.Code).ToHashSet();
string[] specialties = ["Family Medicine", "Internal Medicine", "Pediatrics", "Emergency Medicine", "Cardiology", "Orthopedics",
    "Physical Therapy", "Behavioral Health", "Radiology", "Laboratory", "Gastroenterology", "Pulmonology", "Urgent Care",
    "Durable Medical Equipment", "Hospital Medicine", "Endocrinology"];
string SpecialtyFor(string kind, string code) => kind switch
{
    "er" => "Emergency Medicine", "inpatient" => "Hospital Medicine", "lab" => "Laboratory", "imaging" => "Radiology",
    "therapy" => "Physical Therapy", "behavioral" => "Behavioral Health", "dme" => "Durable Medical Equipment",
    "procedure" => code == "45378" ? "Gastroenterology" : code == "20610" ? "Orthopedics" : "Emergency Medicine",
    "office-test" => code == "95810" || code == "94010" ? "Pulmonology" : "Cardiology",
    _ => Pick(new[] { "Family Medicine", "Internal Medicine", "Family Medicine", "Endocrinology", "Urgent Care" }),
};

// Providers: invented practices with invented 10-digit IDs (the "9" prefix keeps them out of the NPI range).
string[] practiceWords = ["Riverside", "Summit", "Lakeshore", "Northgate", "Evergreen", "Harborview", "Cedar Grove", "Pinecrest",
    "Brookside", "Meadowlark", "Granite", "Silver Creek", "Westfield", "Oakridge", "Bluewater", "Stonebridge"];
var providers = specialties.ToDictionary(s => s, s => Enumerable.Range(0, 6).Select(_ =>
    (Id: $"9{rng.NextInt64(100_000_000, 999_999_999)}",
     Name: $"{Pick(practiceWords)} {s}{(rng.Next(3) == 0 ? " Associates" : rng.Next(2) == 0 ? " Group" : " Clinic")}",
     Tin: $"9{rng.Next(1, 10)}-{rng.Next(1_000_000, 9_999_999)}")).ToList());

// ── Members ──────────────────────────────────────────────────────────

var yearStart = new DateTime(2025, 1, 1);
var yearEnd = new DateTime(2025, 12, 31);
var usedSsns = new HashSet<string>();
var members = new List<Member>();
for (int i = 0; i < MemberCount; i++)
{
    var gender = rng.Next(100) < 51 ? "F" : "M";
    if (rng.Next(200) == 0) gender = "U";
    var first = gender == "F" ? Pick(femaleNames) : gender == "M" ? Pick(maleNames) : Pick(rng.Next(2) == 0 ? femaleNames : maleNames);
    // Ages skew adult, with children and seniors.
    int age = rng.Next(100) switch { < 15 => rng.Next(0, 18), < 75 => rng.Next(18, 65), _ => rng.Next(65, 95) };
    var dob = yearStart.AddYears(-age).AddDays(-rng.Next(0, 365));
    var place = Pick(places);
    string ssn;
    do { ssn = $"{rng.Next(900, 1000)}-{rng.Next(1, 100):00}-{rng.Next(1, 10000):0000}"; } while (!usedSsns.Add(ssn));
    var plan = age >= 65 && rng.Next(3) > 0 ? plans[4] : age < 18 && rng.Next(3) == 0 ? plans[5] : Pick(plans.Take(4).ToList());
    int conditionCount = age switch { < 18 => rng.Next(0, 2), < 45 => rng.Next(0, 3), < 65 => rng.Next(1, 4), _ => rng.Next(2, 5) };
    var conditions = chronic.OrderBy(_ => rng.Next()).Take(conditionCount).ToList();
    members.Add(new Member(
        Id: $"SYN{rng.Next(100_000_000, 999_999_999)}",
        First: first,
        Middle: rng.Next(10) < 8 ? ((char)('A' + rng.Next(26))).ToString() : "",
        Last: Pick(lastNames),
        Gender: gender,
        Dob: dob,
        Street: $"{rng.Next(1, 9900)} {Pick(streetNames)} {Pick(streetTypes)}{(rng.Next(5) == 0 ? " " + Pick(units) : "")}",
        City: place.City, State: place.State, Zip: place.Zip,
        Ssn: ssn,
        Phone: $"({place.Area}) 555-01{rng.Next(0, 100):00}",
        Plan: plan.Name, Group: plan.Group,
        Relationship: age < 18 ? "Child" : rng.Next(4) == 0 ? "Spouse" : "Self",
        Age: age,
        Conditions: conditions,
        Deductible: plan.Name.Contains("HDHP") ? 3000m : plan.Name.Contains("Medicaid") ? 0m : plan.Name.Contains("Medicare") ? 250m : 1000m));
}

// How many lines each member has: uneven, as real claims are (a few heavy users, many light ones), summing to LineCount.
var weights = members.Select(m => Math.Pow(rng.NextDouble(), 2.2) * (1 + m.Conditions.Count) + 0.05).ToArray();
var lineTargets = weights.Select(w => Math.Max(1, (int)Math.Round(w / weights.Sum() * LineCount))).ToArray();
for (int diff = LineCount - lineTargets.Sum(); diff != 0; diff = LineCount - lineTargets.Sum())
{
    int k = rng.Next(MemberCount);
    if (diff > 0) lineTargets[k]++;
    else if (lineTargets[k] > 1) lineTargets[k]--;
}

// ── Claims ───────────────────────────────────────────────────────────

var lines = new List<object[]>(LineCount);
var deductibleMet = members.ToDictionary(m => m.Id, _ => 0m);
long claimNumber = 0;
var claimIds = new HashSet<string>();

for (int mi = 0; mi < members.Count; mi++)
{
    var m = members[mi];
    int remaining = lineTargets[mi];
    var visits = new List<(DateTime Date, List<(string Code, string Description, double Price, int MaxUnits, string Kind, string[] Places)> Lines, string Kind, string Dx1, string Dx1Desc, string Dx2, string Dx2Desc)>();
    while (remaining > 0)
    {
        var date = yearStart.AddDays(rng.Next(0, 365));
        // The kind of visit, and the lines that go with it.
        int roll = rng.Next(100);
        string kind = roll < 46 ? "office" : roll < 54 ? "preventive" : roll < 62 ? "therapy" : roll < 69 ? "behavioral"
            : roll < 77 ? "er" : roll < 81 ? "inpatient" : roll < 89 ? "lab" : roll < 94 ? "imaging" : roll < 97 ? "dme" : "procedure";
        if (kind == "behavioral" && !m.Conditions.Any(c => c.Code.StartsWith('F'))) kind = "office";
        if (kind == "therapy" && m.Age < 12) kind = "office";

        (string Code, string Description) dx = kind switch
        {
            "preventive" => m.Age < 18 ? preventive[1] : Pick(preventive.Where(p => p.Code != "Z00.129").ToList()),
            "behavioral" => m.Conditions.First(c => c.Code.StartsWith('F')),
            "therapy" => Pick(new[] { chronic.First(c => c.Code == "M54.50"), chronic.First(c => c.Code == "M17.11"), acute.First(a => a.Code == "S93.401A"), acute.First(a => a.Code == "M25.561") }),
            _ => m.Conditions.Count > 0 && rng.Next(100) < 55 ? Pick(m.Conditions) : Pick(acute),
        };
        (string Code, string Description) dx2 = m.Conditions.Where(c => c.Code != dx.Code).ToList() is { Count: > 0 } others && rng.Next(100) < 60
            ? Pick(others) : ("", "");

        var of = (string k) => services.Where(s => s.Kind == k).ToList();
        var visitLines = new List<(string Code, string Description, double Price, int MaxUnits, string Kind, string[] Places)>();
        switch (kind)
        {
            case "office":
                visitLines.Add(Pick(of("office").Where(s => s.Code is not ("99203" or "99204") || rng.Next(6) == 0).ToList()));
                if (rng.Next(100) < 35) { visitLines.Add(services.First(s => s.Code == "36415")); visitLines.Add(Pick(of("lab").Where(s => s.Code != "36415").ToList())); }
                if (dx.Code == "J02.9") visitLines.Add(services.First(s => s.Code == "87880"));
                if (dx.Code == "R07.9" || dx.Code == "I48.91") visitLines.Add(services.First(s => s.Code == "93000"));
                if (dx.Code == "J45.909" && rng.Next(3) == 0) visitLines.Add(services.First(s => s.Code == "J7613"));
                if (rng.Next(100) < 8) visitLines.Add(services.First(s => s.Code == "J3420"));
                if ((date.Month >= 9 || date.Month <= 2) && rng.Next(100) < 20) { visitLines.Add(services.First(s => s.Code == "90471")); visitLines.Add(services.First(s => s.Code == "90686")); }
                break;
            case "preventive":
                visitLines.Add(m.Age < 5 ? services.First(s => s.Code == "99392") : m.Age < 40 ? services.First(s => s.Code == "99395")
                    : m.Age < 65 ? services.First(s => s.Code == "99396") : rng.Next(2) == 0 ? services.First(s => s.Code == "G0439") : services.First(s => s.Code == "99397"));
                if (m.Age >= 18) { visitLines.Add(services.First(s => s.Code == "36415")); visitLines.Add(services.First(s => s.Code == "80061")); }
                if (dx.Code == "Z12.31" && m.Gender == "F") visitLines.Add(services.First(s => s.Code == "77067"));
                if (dx.Code == "Z23" || rng.Next(4) == 0) { visitLines.Add(services.First(s => s.Code == "90471")); visitLines.Add(Pick(of("vaccine").Where(s => s.Code != "90471").ToList())); }
                break;
            case "therapy":
                if (rng.Next(6) == 0) visitLines.Add(services.First(s => s.Code == "97161"));
                visitLines.Add(services.First(s => s.Code == "97110"));
                if (rng.Next(2) == 0) visitLines.Add(services.First(s => s.Code == "97140"));
                break;
            case "behavioral":
                visitLines.Add(Pick(of("behavioral")));
                break;
            case "er":
                visitLines.Add(Pick(of("er")));
                if (rng.Next(2) == 0) visitLines.Add(services.First(s => s.Code == "85025"));
                if (dx.Code is "R07.9" or "R05.9" or "J44.9" or "U07.1") visitLines.Add(services.First(s => s.Code == "71046"));
                if (dx.Code == "R07.9") visitLines.Add(services.First(s => s.Code == "93000"));
                if (dx.Code == "R10.9" && rng.Next(2) == 0) visitLines.Add(services.First(s => s.Code == "74177"));
                if (dx.Code == "S93.401A") { visitLines.Add(services.First(s => s.Code == "73610")); visitLines.Add(services.First(s => s.Code == "E0114")); }
                if (dx.Code == "S61.411A") visitLines.Add(services.First(s => s.Code == "12002"));
                if (dx.Code == "U07.1") visitLines.Add(services.First(s => s.Code == "87635"));
                if (rng.Next(4) == 0) visitLines.Add(services.First(s => s.Code == "J1100"));
                break;
            case "inpatient":
                visitLines.Add(services.First(s => s.Code == "99221"));
                for (int d = rng.Next(1, 4); d > 0; d--) visitLines.Add(services.First(s => s.Code == "99232"));
                visitLines.Add(services.First(s => s.Code == "99238"));
                break;
            case "lab":
                visitLines.Add(services.First(s => s.Code == "36415"));
                foreach (var lab in of("lab").Where(s => s.Code is "80053" or "85025" or "83036" or "84443" or "80061").OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)))
                    visitLines.Add(lab);
                if (m.Conditions.Any(c => c.Code == "E11.9")) visitLines.Add(services.First(s => s.Code == "83036"));
                break;
            case "imaging":
                visitLines.Add(Pick(of("imaging").Where(s => s.Code != "77067" || m.Gender == "F").ToList()));
                break;
            case "dme":
                visitLines.Add(m.Conditions.Any(c => c.Code == "E11.9") ? services.First(s => s.Code == "A4253")
                    : m.Conditions.Any(c => c.Code == "G47.33") ? services.First(s => s.Code == "E0601") : services.First(s => s.Code == "E0114"));
                break;
            case "procedure":
                visitLines.Add(Pick(of("procedure").Concat(of("office-test")).ToList()));
                break;
        }
        visitLines = visitLines.DistinctBy(l => l.Code + (l.Code == "99232" ? rng.Next().ToString() : "")).Take(remaining).ToList();
        remaining -= visitLines.Count;
        visits.Add((date, visitLines, kind, dx.Code, dx.Description, dx2.Code, dx2.Description));
    }

    foreach (var visit in visits.OrderBy(v => v.Date))
    {
        string claimId;
        do { claimId = $"CLM25{rng.Next(10_000_000, 99_999_999)}"; } while (!claimIds.Add(claimId));
        claimNumber++;
        var firstLine = visit.Lines[0];
        var pos = Pick(firstLine.Places);
        var specialty = SpecialtyFor(firstLine.Kind == "vaccine" || firstLine.Kind == "drug" ? "office" : firstLine.Kind, firstLine.Code);
        var provider = Pick(providers[specialty]);
        bool institutional = pos is "21" or "22" or "23" or "19" or "24";
        int stayDays = visit.Kind == "inpatient" ? visit.Lines.Count - 1 : 0;
        var start = visit.Date;
        if (start.AddDays(stayDays) > yearEnd) start = yearEnd.AddDays(-stayDays);
        var end = start.AddDays(stayDays);
        if (visit.Kind == "dme" && firstLine.Code == "E0601") end = start.AddDays(Math.Min(29, (yearEnd - start).Days));
        var received = end.AddDays(rng.Next(2, 35));
        int statusRoll = rng.Next(100);
        string status = statusRoll < 86 ? "Paid" : statusRoll < 93 ? "Denied" : statusRoll < 97 ? "Adjusted" : "Pending";
        string denial = status == "Denied" ? Pick(new[] { "CO-16 Missing information", "CO-197 No prior authorization", "CO-50 Not medically necessary", "CO-29 Filing limit expired", "PR-27 Coverage terminated", "CO-18 Duplicate claim" }) : "";
        DateTime? paidDate = status is "Paid" or "Adjusted" or "Denied" ? received.AddDays(rng.Next(5, 40)) : null;
        bool copayTaken = false;

        for (int li = 0; li < visit.Lines.Count; li++)
        {
            var line = visit.Lines[li];
            var lineStart = visit.Kind == "inpatient" ? start.AddDays(Math.Min(li, stayDays)) : start;
            var lineEnd = visit.Kind == "inpatient" ? lineStart : end;
            int unitCount = line.MaxUnits == 1 ? 1 : rng.Next(1, line.MaxUnits + 1);
            decimal unitPrice = Money(line.Price * (0.85 + rng.NextDouble() * 0.35));
            decimal charge = unitPrice * unitCount;
            decimal allowed = status == "Denied" ? 0 : Money((double)charge * (0.50 + rng.NextDouble() * 0.30));
            if (m.Plan.Contains("Medicaid")) allowed = Money((double)allowed * 0.8);

            // Copay once a claim, on the visit line, for visits that carry one; then deductible, then coinsurance.
            decimal copay = 0, deductible = 0, coinsurance = 0;
            bool preventiveLine = visit.Kind == "preventive" || line.Kind == "vaccine";
            if (status != "Denied" && !preventiveLine && !m.Plan.Contains("Medicaid"))
            {
                if (!copayTaken && line.Kind is "office" or "er" or "behavioral" or "therapy" && !m.Plan.Contains("HDHP"))
                {
                    copay = Math.Min(allowed, line.Kind switch
                    {
                        "er" => 150m, "therapy" => 30m, "behavioral" => 25m,
                        _ => pos is "02" or "10" ? 10m : line.Code is "99215" or "99204" ? 40m : 25m,
                    });
                    copayTaken = true;
                }
                else if (line.Kind is not ("office" or "behavioral") || m.Plan.Contains("HDHP"))
                {
                    deductible = Math.Min(allowed, Math.Max(0, m.Deductible - deductibleMet[m.Id]));
                    deductibleMet[m.Id] += deductible;
                    coinsurance = Money((double)(allowed - deductible) * (m.Plan.Contains("Medicare") ? 0.20 : m.Plan.Contains("HMO") ? 0.10 : 0.20));
                }
            }
            if (status == "Pending") { allowed = 0; copay = 0; deductible = 0; coinsurance = 0; }
            decimal memberResponsibility = copay + deductible + coinsurance;
            decimal paid = status is "Paid" or "Adjusted" ? Math.Max(0, allowed - memberResponsibility) : 0;

            lines.Add([
                claimId, li + 1, institutional ? "Institutional" : "Professional", status, denial,
                m.First, m.Last, m.Middle, m.Gender, m.Dob, m.Street, m.City, m.State, m.Zip, m.Ssn, m.Id, m.Phone,
                m.Plan, m.Group, m.Relationship,
                lineStart, lineEnd, line.Code, line.Description, visit.Dx1, visit.Dx1Desc, visit.Dx2, visit.Dx2Desc,
                unitCount, unitPrice, charge, allowed, deductible, coinsurance, copay, memberResponsibility, paid,
                pos, placeNames[pos], provider.Id, provider.Name, specialty, provider.Tin,
                received, (object?)paidDate ?? "",
            ]);
        }
    }
}

// ── Workbook ─────────────────────────────────────────────────────────

string[] headers =
[
    "Claim ID", "Claim Line", "Claim Type", "Claim Status", "Denial Reason",
    "Member First Name", "Member Last Name", "Member Middle Initial", "Member Gender", "Member DOB",
    "Member Street Address", "Member City", "Member State", "Member Zip", "Member SSN", "Member Insurance ID", "Member Phone",
    "Plan Name", "Group Number", "Relationship To Subscriber",
    "Service Date Start", "Service Date End", "Service Rendered (HCPCS Code)", "Service Description",
    "Member Diag Primary (ICD-10)", "Primary Diag Description", "Member Diag Secondary (ICD-10)", "Secondary Diag Description",
    "Served Units", "Unit Price", "Total Cost", "Allowed Amount", "Deductible", "Coinsurance", "Member Copay",
    "Member Responsibility", "Plan Paid", "Place Of Service Code", "Place Of Service",
    "Rendering Provider ID", "Rendering Provider Name", "Provider Specialty", "Billing Provider TIN",
    "Claim Received Date", "Claim Processed Date",
];

using var workbook = new XLWorkbook();
var sheet = workbook.Worksheets.Add("Claims");
for (int c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
// Sorted by service date, then claim and line, as a claims extract usually is.
var ordered = lines.OrderBy(l => (DateTime)l[20]).ThenBy(l => (string)l[0]).ThenBy(l => (int)l[1]).ToList();
for (int r = 0; r < ordered.Count; r++)
    for (int c = 0; c < headers.Length; c++)
        sheet.Cell(r + 2, c + 1).Value = ordered[r][c] switch
        {
            DateTime d => d, decimal d => d, int i => i, string s => s, null => "", var o => o.ToString(),
        };

var header = sheet.Range(1, 1, 1, headers.Length);
header.Style.Font.Bold = true;
header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
foreach (var c in new[] { 10, 21, 22, 44, 45 }) sheet.Column(c).Style.NumberFormat.Format = "yyyy-mm-dd";
foreach (var c in Enumerable.Range(30, 8)) sheet.Column(c).Style.NumberFormat.Format = "#,##0.00";
foreach (var c in new[] { 14, 38 }) sheet.Column(c).Style.NumberFormat.Format = "@";   // ZIPs and POS codes keep leading zeros
sheet.SheetView.FreezeRows(1);
sheet.Range(1, 1, ordered.Count + 1, headers.Length).SetAutoFilter();
sheet.Columns().AdjustToContents(1, 300);

var about = workbook.Worksheets.Add("About");
string[][] notes =
[
    ["Contents", $"{ordered.Count:N0} contrived medical claim lines in {claimNumber:N0} claims for {MemberCount} contrived members, service dates 2025-01-01 to 2025-12-31."],
    ["Synthetic", "Every member, address, phone, SSN, insurance ID, provider and claim is invented. No real person's data is included."],
    ["SSNs", "All start 900-999, an area number the Social Security Administration never issues."],
    ["Phones", "All are 555-0100 to 555-0199, the range reserved for fictional use."],
    ["IDs", "Insurance IDs start SYN, claim IDs CLM25, provider IDs 9 (outside the NPI range); TINs are random."],
    ["Addresses", "Street addresses are invented; the city, state and ZIP combinations are real places."],
    ["Codes", "HCPCS/CPT, ICD-10-CM and place-of-service codes are real code values; descriptions are short paraphrases."],
    ["Dates", "Claim Processed Date is when the claim was paid, adjusted or denied; it's blank while a claim is pending."],
    ["Money", "Total Cost = Served Units x Unit Price. Member Responsibility = Deductible + Coinsurance + Copay. Plan Paid = Allowed - Member Responsibility (0 when denied or pending)."],
    ["Testing", "Each member appears on many lines, so the member columns repeat. To try Vector Match Engine, vectorize the member name, DOB and address columns and match this sheet against a second extract, or against itself."],
    ["Regenerate", "dotnet run SAMPLES/GenerateClaimsSample.cs (fixed seed: the same workbook each time)."],
];
for (int r = 0; r < notes.Length; r++) { about.Cell(r + 1, 1).Value = notes[r][0]; about.Cell(r + 1, 2).Value = notes[r][1]; }
about.Column(1).Style.Font.Bold = true;
about.Column(1).Width = 14;
about.Column(2).Width = 120;
about.Column(2).Style.Alignment.WrapText = true;

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
workbook.SaveAs(output);
Console.WriteLine($"Wrote {output}: {ordered.Count:N0} lines, {claimNumber:N0} claims, {members.Count} members, " +
    $"service dates {ordered.Min(l => (DateTime)l[20]):yyyy-MM-dd} to {ordered.Max(l => (DateTime)l[21]):yyyy-MM-dd}.");

record Member(string Id, string First, string Middle, string Last, string Gender, DateTime Dob, string Street, string City,
    string State, string Zip, string Ssn, string Phone, string Plan, string Group, string Relationship, int Age,
    List<(string Code, string Description)> Conditions, decimal Deductible);
