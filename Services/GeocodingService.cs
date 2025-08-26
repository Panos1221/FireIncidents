using System.Text.Json;
using FireIncidents.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FireIncidents.Services
{
    public class GeocodingService
    {
        private readonly ILogger<GeocodingService> _logger;
        private readonly IMemoryCache _cache;
        private readonly string _nominatimBaseUrl = "https://nominatim.openstreetmap.org/search";

        // center of Greece
        private readonly double _defaultLat = 38.2;
        private readonly double _defaultLon = 23.8;

        // municipality mappings
        private Dictionary<string, (double Lat, double Lon)> _municipalityCoordinates;

        // Track incident coordinates to avoid placing them on top of each other
        private Dictionary<string, List<(double Lat, double Lon)>> _activeIncidentCoordinates;

        // Predefined coordinates for common regions
        private readonly Dictionary<string, (double Lat, double Lon)> _regionCoordinates = new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase)
        {
            { "ΠΕΡΙΦΕΡΕΙΑ ΑΤΤΙΚΗΣ", (37.9838, 23.7275) },                      // Athens
            { "ΠΕΡΙΦΕΡΕΙΑ ΚΕΝΤΡΙΚΗΣ ΜΑΚΕΔΟΝΙΑΣ", (40.6401, 22.9444) },        // Thessaloniki
            { "ΠΕΡΙΦΕΡΕΙΑ ΔΥΤΙΚΗΣ ΕΛΛΑΔΑΣ", (38.2466, 21.7359) },             // Patras
            { "ΠΕΡΙΦΕΡΕΙΑ ΘΕΣΣΑΛΙΑΣ", (39.6383, 22.4179) },                    // Larissa
            { "ΠΕΡΙΦΕΡΕΙΑ ΚΡΗΤΗΣ", (35.3387, 25.1442) },                       // Heraklion
            { "ΠΕΡΙΦΕΡΕΙΑ ΑΝΑΤΟΛΙΚΗΣ ΜΑΚΕΔΟΝΙΑΣ ΚΑΙ ΘΡΑΚΗΣ", (41.1169, 25.4045) }, // Komotini
            { "ΠΕΡΙΦΕΡΕΙΑ ΗΠΕΙΡΟΥ", (39.6675, 20.8511) },                      // Ioannina
            { "ΠΕΡΙΦΕΡΕΙΑ ΠΕΛΟΠΟΝΝΗΣΟΥ", (37.5047, 22.3742) },                 // Tripoli
            { "ΠΕΡΙΦΕΡΕΙΑ ΔΥΤΙΚΗΣ ΜΑΚΕΔΟΝΙΑΣ", (40.3007, 21.7887) },           // Kozani
            { "ΠΕΡΙΦΕΡΕΙΑ ΣΤΕΡΕΑΣ ΕΛΛΑΔΑΣ", (38.9, 22.4331) },                 // Lamia
            { "ΠΕΡΙΦΕΡΕΙΑ ΒΟΡΕΙΟΥ ΑΙΓΑΙΟΥ", (39.1, 26.5547) },                 // Mytilene
            { "ΠΕΡΙΦΕΡΕΙΑ ΝΟΤΙΟΥ ΑΙΓΑΙΟΥ", (36.4335, 28.2183) },              // Rhodes
            { "ΠΕΡΙΦΕΡΕΙΑ ΙΟΝΙΩΝ ΝΗΣΩΝ", (39.6243, 19.9217) }                 // Corfu
        };

        public GeocodingService(ILogger<GeocodingService> logger, IHttpClientFactory httpClientFactory, IMemoryCache cache)
        {
            _logger = logger;
            _cache = cache;

            // Initialize the municipality coordinates dictionary
            InitializeMunicipalityCoordinates();

            // Initialize the active incident coordinates tracker
            _activeIncidentCoordinates = new Dictionary<string, List<(double Lat, double Lon)>>();
        }

        // Clear active incidents when starting a new fetch cycle
        public void ClearActiveIncidents()
        {
            _activeIncidentCoordinates.Clear();
        }

        // Initialize the municipality coordinates database
        private void InitializeMunicipalityCoordinates()
        {
            _municipalityCoordinates = new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase);

            LoadBuiltInMunicipalityData();

            // load from JSON file if it exists
            try
            {
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "municipalities.json");
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath, Encoding.UTF8);
                    var data = JsonSerializer.Deserialize<Dictionary<string, (double Lat, double Lon)>>(json);

                    if (data != null)
                    {
                        foreach (var item in data)
                        {
                            if (!_municipalityCoordinates.ContainsKey(item.Key))
                            {
                                _municipalityCoordinates[item.Key] = item.Value;
                            }
                        }

                        _logger.LogInformation($"Loaded {data.Count} municipalities from JSON file");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading municipality data from JSON file");
            }

            _logger.LogInformation($"Initialized with {_municipalityCoordinates.Count} municipality coordinates");
        }

        // built-in municipality data (abbreviated for space)
        private void LoadBuiltInMunicipalityData()
        {
            // ATTIKI municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΑΘΗΝΑΙΩΝ"] = (37.9838, 23.7275);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΕΙΡΑΙΩΣ"] = (37.9432, 23.6469);
            _municipalityCoordinates["ΔΗΜΟΣ ΓΛΥΦΑΔΑΣ"] = (37.8685, 23.7545);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΕΡΙΣΤΕΡΙΟΥ"] = (38.0133, 23.6913);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΛΛΙΘΕΑΣ"] = (37.9595, 23.7085);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΑΛΛΗΝΗΣ"] = (38.0054, 23.8791);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΜΑΡΟΥΣΙΟΥ"] = (38.0562, 23.8083);
            _municipalityCoordinates["ΔΗΜΟΣ ΧΑΛΑΝΔΡΙΟΥ"] = (38.0227, 23.7965);
            _municipalityCoordinates["ΔΗΜΟΣ ΗΛΙΟΥΠΟΛΕΩΣ"] = (37.9304, 23.7490);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΗΦΙΣΙΑΣ"] = (38.0809, 23.8081);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΙΓΑΛΕΩ"] = (38.0004, 23.6746);
            _municipalityCoordinates["ΔΗΜΟΣ ΙΛΙΟΥ"] = (38.0337, 23.6994);
            _municipalityCoordinates["ΔΗΜΟΣ ΒΥΡΩΝΟΣ"] = (37.9571, 23.7503);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΧΑΡΝΩΝ"] = (38.0836, 23.7402);
            _municipalityCoordinates["ΔΗΜΟΣ ΝΕΑΣ ΣΜΥΡΝΗΣ"] = (37.9452, 23.7171);
            _municipalityCoordinates["ΔΗΜΟΣ ΝΙΚΑΙΑΣ"] = (37.9661, 23.6434);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΓΙΩΝ ΑΝΑΡΓΥΡΩΝ"] = (38.0274, 23.7234);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΕΡΑΤΣΙΝΙΟΥ"] = (37.9660, 23.6276);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΛΙΜΟΥ"] = (37.9131, 23.7407);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΡΓΥΡΟΥΠΟΛΕΩΣ"] = (37.9049, 23.7505);
            _municipalityCoordinates["ΔΗΜΟΣ ΖΩΓΡΑΦΟΥ"] = (37.9803, 23.7710);
            _municipalityCoordinates["ΔΗΜΟΣ ΓΑΛΑΤΣΙΟΥ"] = (38.0201, 23.7529);
            _municipalityCoordinates["ΔΗΜΟΣ ΝΕΑΣ ΙΩΝΙΑΣ"] = (38.0376, 23.7594);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΑΛΑΙΟΥ ΦΑΛΗΡΟΥ"] = (37.9310, 23.7013);
            _municipalityCoordinates["ΔΗΜΟΣ ΜΕΤΑΜΟΡΦΩΣΕΩΣ"] = (38.0610, 23.7608);
            _municipalityCoordinates["ΔΗΜΟΣ ΛΑΥΡΕΩΤΙΚΗΣ"] = (37.7165, 24.0602);
            _municipalityCoordinates["ΔΗΜΟΣ ΣΠΑΤΩΝ"] = (37.9534, 23.9014);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΣΠΡΟΠΥΡΓΟΥ"] = (38.0597, 23.5988);
            _municipalityCoordinates["ΔΗΜΟΣ ΡΑΦΗΝΑΣ"] = (38.0218, 24.0097);
            _municipalityCoordinates["ΔΗΜΟΣ ΜΕΓΑΡΕΩΝ"] = (38.0056, 23.3399);
            _municipalityCoordinates["ΔΗΜΟΣ ΕΛΕΥΣΙΝΑΣ"] = (38.0418, 23.5413);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΡΩΠΙΑΣ"] = (37.9990, 23.8651);
            _municipalityCoordinates["ΔΗΜΟΣ ΣΑΛΑΜΙΝΟΣ"] = (37.9392, 23.5016);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΓΙΟΥ ΔΗΜΗΤΡΙΟΥ"] = (37.9356, 23.7339);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΓΙΑΣ ΠΑΡΑΣΚΕΥΗΣ"] = (38.0113, 23.8195);
            _municipalityCoordinates["ΔΗΜΟΣ ΜΑΝΔΡΑΣ-ΕΙΔΥΛΛΙΑΣ"] = (38.1450, 23.4950);
            _municipalityCoordinates["ΔΗΜΟΣ ΦΥΛΗΣ"] = (38.1240, 23.6700);
            _municipalityCoordinates["ΔΗΜΟΣ ΜΟΣΧΑΤΟΥ-ΤΑΥΡΟΥ"] = (37.9500, 23.7000); // Moschato-Tavros
            _municipalityCoordinates["ΔΗΜΟΣ ΠΕΤΡΟΥΠΟΛΗΣ"] = (38.0130, 23.6820);     // Petroupoli
            _municipalityCoordinates["ΔΗΜΟΣ ΣΠΑΤΩΝ-ΑΡΤΕΜΙΔΟΣ"] = (37.9534, 23.9014); // Spata-Artemida
            _municipalityCoordinates["ΔΗΜΟΣ ΡΑΦΗΝΑΣ-ΠΙΚΕΡΜΙΟΥ"] = (38.0218, 24.0097); // Rafina-Pikermi

            _municipalityCoordinates["ΔΗΜΟΣ ΟΡΩΠΟΥ"] = (38.1667, 23.7667);        // Oropos
            _municipalityCoordinates["ΔΗΜΟΣ ΜΑΡΑΘΩΝΑ"] = (38.1500, 23.9667);       // Marathon
            _municipalityCoordinates["ΔΗΜΟΣ ΣΑΡΩΝΙΚΟΥ"] = (37.8000, 23.9000);      // Saronikos

            _municipalityCoordinates["ΔΗΜΟΣ ΛΙΒΑΔΕΙΑΣ"] = (38.4333, 23.1778);
            _municipalityCoordinates["ΔΗΜΟΣ ΘΗΒΑΙΩΝ"] = (38.2322, 23.3194);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΡΑΧΟΒΑΣ-ΔΙΣΤΟΜΟΥ"] = (38.4170, 23.3600);
            _municipalityCoordinates["ΔΗΜΟΣ ΛΑΜΙΕΩΝ"] = (38.9023, 22.4323);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΜΦΙΣΣΑΣ"] = (38.5600, 22.2200);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΡΠΕΝΗΣΙΟΥ"] = (38.9170, 21.6167);
            _municipalityCoordinates["ΔΗΜΟΣ ΔΩΡΙΔΑΣ"] = (38.5833, 22.0167);
            _municipalityCoordinates["ΔΗΜΟΣ ΔΕΛΦΩΝ"] = (38.4833, 22.5000);

            //Central Greece municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΟΡΧΟΜΕΝΟΥ"] = (38.4228, 23.5981);      // Orchomenos
            _municipalityCoordinates["ΔΗΜΟΣ ΑΛΙΑΡΤΟΥ-ΘΕΣΠΙΕΩΝ"] = (38.3333, 23.3167); // Aliartos-Thespies
            _municipalityCoordinates["ΔΗΜΟΣ ΤΑΝΑΓΡΑΣ"] = (38.3167, 23.5833);        // Tanagra
            _municipalityCoordinates["ΔΗΜΟΣ ΑΜΦΙΣΣΑΣ"] = (38.5556, 22.3236);        // Amfissa (Delphi)
            _municipalityCoordinates["ΔΗΜΟΣ ΑΛΙΜΟΥΝΑΣ"] = (38.5167, 22.3667);       // small area
            _municipalityCoordinates["ΔΗΜΟΣ ΣΚΑΛΑΣ"] = (38.4500, 22.4500);           // small area
            _municipalityCoordinates["ΔΗΜΟΣ ΑΓΡΙΝΙΟΥ"] = (38.6167, 21.4000);         // Agrinio (technically Aitoloakarnania, but some border areas)
            _municipalityCoordinates["ΠΕΡΙΦΕΡΕΙΑ ΣΤΕΡΕΑΣ ΕΛΛΑΔΑΣ-Δ. ΔΟΜΟΚΟΥ - ΞΥΝΙΑΔΟΣ"] = (39.07288671912797, 22.213581770475724);
            _municipalityCoordinates["ΔΟΜΟΚΟΥ - ΞΥΝΙΑΔΟΣ"] = (39.07288671912797, 22.213581770475724);
            _municipalityCoordinates["ΔΗΜΟΣ ΔΟΜΟΚΟΥ - ΞΥΝΙΑΔΟΣ"] = (39.07288671912797, 22.213581770475724);
            _municipalityCoordinates["ΔΗΜΟΣ ΞΥΝΙΑΔΟΣ"] = (39.07288671912797, 22.213581770475724);
            _municipalityCoordinates["ΔΗΜΟΣ ΔΟΜΟΚΟΥ "] = (39.12924467759778, 22.297960797566027);


            // Phocis (Φωκίδα)
            _municipalityCoordinates["ΔΗΜΟΣ ΔΕΛΦΩΝ"] = (38.4833, 22.5000);          // Delphi
            _municipalityCoordinates["ΔΗΜΟΣ ΙΤΕΑΣ"] = (38.3500, 22.5167);           // Itea

            // THESSALONIKI municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΘΕΣΣΑΛΟΝΙΚΗΣ"] = (40.6401, 22.9444);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΛΑΜΑΡΙΑΣ"] = (40.5762, 22.9486);
            _municipalityCoordinates["ΔΗΜΟΣ ΣΥΚΕΩΝ"] = (40.6374, 22.9508);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΑΥΛΟΥ ΜΕΛΑ"] = (40.6717, 22.9021);
            _municipalityCoordinates["ΔΗΜΟΣ ΝΕΑΠΟΛΗΣ"] = (40.6523, 22.9175);
            _municipalityCoordinates["ΔΗΜΟΣ ΣΤΑΥΡΟΥΠΟΛΕΩΣ"] = (40.6623, 22.9098);
            _municipalityCoordinates["ΔΗΜΟΣ ΕΥΟΣΜΟΥ"] = (40.6640, 22.9087);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΜΠΕΛΟΚΗΠΩΝ"] = (40.6580, 22.9091);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΟΛΙΧΝΗΣ"] = (40.6646, 22.9335);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΥΛΑΙΑΣ"] = (40.5964, 22.9817);
            _municipalityCoordinates["ΔΗΜΟΣ ΘΕΡΜΗΣ"] = (40.5236, 23.0128);

            _municipalityCoordinates["ΔΗΜΟΣ ΕΥΚΑΡΠΙΑΣ"] = (40.6333, 22.9000);     // Efkarpia
            _municipalityCoordinates["ΔΗΜΟΣ ΑΜΠΕΛΟΚΗΠΩΝ-ΜΕΝΕΜΕΝΗΣ"] = (40.6580, 22.9091); // Ampelokipoi-Menemeni
            _municipalityCoordinates["ΔΗΜΟΣ ΠΕΡΑΙΑΣ"] = (40.5500, 22.9500);       // Peraia
            _municipalityCoordinates["ΔΗΜΟΣ ΘΕΡΜΑΪΚΟΥ"] = (40.5000, 22.9500);      // Thermaikos
            _municipalityCoordinates["ΔΗΜΟΣ ΝΙΚΗΤΗ"] = (40.5000, 22.9500);         

            // PATRAS municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΠΑΤΡΕΩΝ"] = (38.2466, 21.7359);
            _municipalityCoordinates["ΔΗΜΟΣ ΡΙΟΥ"] = (38.3030, 21.7868);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΙΓΙΟΥ"] = (38.2500, 22.0833);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΛΑΒΡΥΤΩΝ"] = (38.0333, 22.1167);     // Kalavryta
            _municipalityCoordinates["ΔΗΜΟΣ ΔΥΤΙΚΗΣ ΑΧΑΪΑΣ"] = (38.2000, 21.8000); // West Achaia
            _municipalityCoordinates["ΔΗΜΟΣ ΕΡΓΑΣΙΑΚΟΥ"] = (38.2500, 21.7333);
            _municipalityCoordinates["ΔΥΜΗΣ"] = (38.133697371302084, 21.550266484711308);


            // HERAKLION municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΗΡΑΚΛΕΙΟΥ"] = (35.3387, 25.1442);
            _municipalityCoordinates["ΔΗΜΟΣ ΧΑΝΙΩΝ"] = (35.5138, 24.0180);
            _municipalityCoordinates["ΔΗΜΟΣ ΡΕΘΥΜΝΗΣ"] = (35.3667, 24.4833);

            // LARISSA municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΛΑΡΙΣΑΙΩΝ"] = (39.6383, 22.4179);
            _municipalityCoordinates["ΔΗΜΟΣ ΒΟΛΟΥ"] = (39.3662, 22.9360);
            _municipalityCoordinates["ΔΗΜΟΣ ΤΡΙΚΚΑΙΩΝ"] = (39.5555, 21.7666);
            _municipalityCoordinates["ΔΗΜΟΣ ΜΟΥΖΑΚΙΟΥ"] = (39.3667, 21.8667);
            _municipalityCoordinates["ΔΗΜΟΣ ΕΛΑΣΣΟΝΑΣ"] = (39.8667, 22.0667);
            _municipalityCoordinates["ΔΗΜΟΣ ΤΡΙΚΑΛΩΝ"] = (39.5556, 21.7670);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΡΔΙΤΣΑΣ"] = (39.3653, 21.9210);

            // WEST GREECE municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΑΡΧΑΙΑΣ ΟΛΥΜΠΙΑΣ"] = (37.6441, 21.6245);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΙΓΙΑΛΕΙΑΣ"] = (38.2500, 22.0833);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΥΡΓΟΥ"] = (37.6750, 21.4367);
            _municipalityCoordinates["ΔΗΜΟΣ ΗΛΙΔΑΣ"] = (37.9404, 21.3700);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΝΔΡΙΤΣΑΙΝΑΣ"] = (37.4896, 21.8798);
            _municipalityCoordinates["ΔΗΜΟΣ ΖΑΧΑΡΩΣ"] = (37.4882, 21.6505);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΗΝΕΙΟΥ"] = (38.0333, 21.4667);

            // HPEIROS municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΑΡΤΑΙΩΝ"] = (39.1566, 20.9877);
            _municipalityCoordinates["ΔΗΜΟΣ ΙΩΑΝΝΙΤΩΝ"] = (39.6675, 20.8511);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΡΕΒΕΖΗΣ"] = (38.9597, 20.7517);
            _municipalityCoordinates["ΔΗΜΟΣ ΗΓΟΥΜΕΝΙΤΣΑΣ"] = (39.5070, 20.2656);
            _municipalityCoordinates["ΔΗΜΟΣ ΖΙΤΣΑΣ"] = (39.7000, 20.8667);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΩΓΩΝΙΟΥ"] = (39.8667, 20.3833);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΑΡΑΜΥΘΙΑΣ"] = (39.5833, 20.2167);
            _municipalityCoordinates["ΒΟΒΟΥΣΗΣ"] = (39.93779124127118, 21.047165770126973);
            _municipalityCoordinates["ΖΑΓΟΡΙΟΥ - ΒΟΒΟΥΣΗΣ"] = (39.93779124127118, 21.047165770126973);
            _municipalityCoordinates["ΔΗΜΟΣ ΖΑΓΟΡΙΟΥ - ΔΗΜΟΤΙΚΗ ΕΝΟΤΗΤΑ ΒΟΒΟΥΣΑΣ"] = (39.93779124127118, 21.047165770126973);           
            _municipalityCoordinates["ΠΕΡΙΦΕΡΕΙΑ ΗΠΕΙΡΟΥ-Δ.ΖΑΓΟΡΙΟΥ - ΒΟΒΟΥΣΗΣ"] = (39.93779124127118, 21.047165770126973);

            // PELOPONNESE municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΤΡΙΦΥΛΙΑΣ"] = (37.1167, 21.5833);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΛΑΜΑΤΑΣ"] = (37.0389, 22.1142);
            _municipalityCoordinates["ΔΗΜΟΣ ΕΡΜΙΟΝΙΔΑΣ"] = (37.3833, 23.2500);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΥΛΟΥ"] = (36.9131, 21.6961);
            _municipalityCoordinates["ΔΗΜΟΣ ΕΥΡΩΤΑ"] = (36.8667, 22.6667);

            _municipalityCoordinates["ΔΗΜΟΣ ΑΡΤΑΙΩΝ - ΑΜΒΡΑΚΙΚΟΥ"] = (39.0464, 20.9026);
            _municipalityCoordinates["ΔΗΜΟΣ ΔΙΑΚΟΠΤΟΥ"] = (38.1997, 22.2027);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΙΓΙΑΛΕΙΑΣ - ΔΙΑΚΟΠΤΟΥ"] = (38.1997, 22.2027);
            _municipalityCoordinates["ΔΗΜΟΣ ΤΡΙΦΥΛΙΑΣ - ΓΑΡΓΑΛΙΑΝΩΝ"] = (37.0643, 21.6428);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΛΑΤΑΝΙΑ - ΒΟΥΚΟΛΙΩΝ"] = (35.4672, 23.7542);
            _municipalityCoordinates["ΔΗΜΟΣ ΣΗΤΕΙΑΣ - ΛΕΥΚΗΣ"] = (35.1000, 26.1000);
            _municipalityCoordinates["ΔΗΜΟΣ ΔΕΛΤΑ - ΕΧΕΔΩΡΟΥ"] = (40.6962, 22.7815);
            _municipalityCoordinates["ΔΗΜΟΣ ΜΕΓΑΡΑ"] = (38.0056, 23.3399);

            _municipalityCoordinates["ΔΗΜΟΣ ΤΟΛΟΥ"] = (37.4833, 23.0167);         // Tolo
            _municipalityCoordinates["ΔΗΜΟΣ ΒΥΤΙΝΑΣ"] = (37.4333, 22.0833);      // Vytina
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΡΥΤΑΙΝΑΣ"] = (37.4500, 22.0333);    // Karytaina
            _municipalityCoordinates["ΔΗΜΟΣ ΜΟΝΕΜΒΑΣΙΑΣ"] = (36.6961, 23.0469);   // Monemvasia (technically Laconia border)
            _municipalityCoordinates["ΔΗΜΟΣ ΓΟΡΤΥΝΙΑΣ"] = (37.0650, 22.1000);     // Gortynia (partly Arcadia)
            _municipalityCoordinates["ΔΗΜΟΣ ΛΟΥΤΡΑΚΙΟΥ-ΠΕΡΑΧΩΡΑΣ"] = (37.9500, 22.9000); // Loutraki-Perachora
            _municipalityCoordinates["ΔΗΜΟΣ ΝΕΜΕΑΣ"] = (37.8833, 22.7333);        // Nemea
            _municipalityCoordinates["ΔΗΜΟΣ ΣΙΚΥΩΝΙΩΝ"] = (37.9500, 22.8333);      // Sikyona
            _municipalityCoordinates["ΔΗΜΟΣ ΜΟΝΕΜΒΑΣΙΑΣ"] = (36.6961, 23.0469);   // Monemvasia
            _municipalityCoordinates["ΔΗΜΟΣ ΜΑΝΗΣ"] = (36.7000, 22.4000);          // Mani
            _municipalityCoordinates["ΔΗΜΟΣ ΓΥΘΕΙΟΥ"] = (36.7500, 22.5667);       // Gytheio
            _municipalityCoordinates["ΔΗΜΟΣ ΕΛΑΦΟΝΗΣΟΥ"] = (36.6333, 22.8833);    // Elafonisos
            _municipalityCoordinates["ΔΗΜΟΣ ΚΥΠΑΡΙΣΣΙΑΣ"] = (36.9500, 21.7000);   // Kyparissia
            _municipalityCoordinates["ΔΗΜΟΣ ΟΙΧΑΛΙΑΣ"] = (37.0667, 22.0167);      // Oichalia
            _municipalityCoordinates["ΔΗΜΟΣ ΦΙΛΙΑΤΡΩΝ"] = (36.9000, 21.7500);     // Filiatra
            _municipalityCoordinates["ΔΗΜΟΣ ΑΝΔΡΙΤΣΑΙΝΑΣ-ΚΡΕΣΤΕΝΩΝ"] = (37.4896, 21.8798); // exists partially

            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΛΑΒΡΥΤΩΝ"] = (38.0333, 22.1167);    // Kalavryta
            _municipalityCoordinates["ΔΗΜΟΣ ΕΡΙΚΕΑΣ"] = (38.0500, 22.0833);        // Erikea (small area)
            _municipalityCoordinates["ΔΗΜΟΣ ΕΡΓΑΣΙΑΚΟΥ"] = (38.2500, 21.7333);     // small area

            // CRETE municipalities 
            _municipalityCoordinates["ΔΗΜΟΣ ΑΡΧΑΝΩΝ"] = (35.1917, 25.1539);
            _municipalityCoordinates["ΔΗΜΟΣ ΒΙΑΝΝΟΥ"] = (35.0539, 25.4058);
            _municipalityCoordinates["ΔΗΜΟΣ ΓΟΡΤΥΝΑΣ"] = (35.0653, 24.9461);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΙΣΣΑΜΟΥ"] = (35.4944, 23.6543);
            _municipalityCoordinates["ΔΗΜΟΣ ΜΑΛΕΒΙΖΙΟΥ"] = (35.3264, 24.9928);
            _municipalityCoordinates["ΔΗΜΟΣ ΜΙΝΩΑ ΠΕΔΙΑΔΑΣ"] = (35.2000, 25.3833);
            _municipalityCoordinates["ΔΗΜΟΣ ΠΛΑΤΑΝΙΑ"] = (35.5156, 23.8700);
            _municipalityCoordinates["ΔΗΜΟΣ ΣΗΤΕΙΑΣ"] = (35.2000, 26.1000);
            _municipalityCoordinates["ΔΗΜΟΣ ΦΑΙΣΤΟΥ"] = (35.0644, 24.8069);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΓΙΟΥ ΝΙΚΟΛΑΟΥ"] = (35.1900, 25.7200);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΠΟΚΟΡΩΝΟΥ"] = (35.4000, 24.2000);
            _municipalityCoordinates["ΔΗΜΟΣ ΑΡΧΑΝΩΝ-ΑΣΚΛΗΠΙΟΥ"] = (35.1917, 25.1539);


            // Argolis (Άργολίδα)
            _municipalityCoordinates["ΔΗΜΟΣ ΑΡΓΟΥ"] = (37.6333, 22.7333);   // Argos
            _municipalityCoordinates["ΔΗΜΟΣ ΝΑΥΠΛΙΟΥ"] = (37.5670, 23.0000);  // Nafplio
            _municipalityCoordinates["ΔΗΜΟΣ ΕΠΙΔΑΥΡΟΥ"] = (37.6000, 22.8500); // Epidaurus
            // Arcadia (Αρκαδία)
            _municipalityCoordinates["ΔΗΜΟΣ ΤΡΙΠΟΛΗΣ"] = (37.3233, 22.1394);    // Tripoli (Arcadia)
            _municipalityCoordinates["ΔΗΜΟΣ ΜΕΓΑΛΟΠΟΛΗΣ"] = (37.2656, 22.2847);   // Megalopolis
            // Corinthia (Κορινθία)
            _municipalityCoordinates["ΔΗΜΟΣ ΚΟΡΙΝΘΟΥ"] = (37.9333, 22.9500);     // Corinth
            // Laconia (Λακωνία)
            _municipalityCoordinates["ΔΗΜΟΣ ΣΠΑΡΤΗΣ"] = (37.0739, 22.4229);       // Sparta
            // Messenia (Μεσσηνία)
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΛΑΜΑΤΑΣ"] = (37.0389, 22.1142);     // Kalamata
            _municipalityCoordinates["ΔΗΜΟΣ ΠΥΛΗΣ"] = (36.9000, 21.8000);          // Pylos
            _municipalityCoordinates["ΔΗΜΟΣ ΜΕΣΗΝΙΑΣ"] = (37.1333, 21.7833);       // Messini
            _municipalityCoordinates["ΜΕΣΣΗΝΗΣ - ΙΘΩΜΗΣ"] = (37.05507163581724, 22.008241198125248);       // Messini
            _municipalityCoordinates["ΜΕΣΣΗΝΗΣ"] = (37.05507163581724, 22.008241198125248);       // Messini
            _municipalityCoordinates["ΙΘΩΜΗΣ"] = (37.05507163581724, 22.008241198125248);       // Messini
            _municipalityCoordinates["Δ. ΜΕΣΣΗΝΗΣ - ΙΘΩΜΗΣ, ΠΕΛΟΠΟΝΝΗΣΟΥ, Greece"] = (37.05507163581724, 22.008241198125248);       // Messini
            _municipalityCoordinates["Δ. ΜΕΣΣΗΝΗΣ - ΙΘΩΜΗΣ"] = (37.05507163581724, 22.008241198125248);       // Messini


            // Aegean Sea municipalities (North Aegean)
            _municipalityCoordinates["ΔΗΜΟΣ ΜΥΤΙΛΗΝΗΣ"] = (39.1100, 26.5547); // Mytilene, Lesvos
            _municipalityCoordinates["ΔΗΜΟΣ ΧΙΟΥ"] = (38.3670, 26.1356);      // Chios
            _municipalityCoordinates["ΔΗΜΟΣ ΒΑΘΟΥ"] = (37.7547, 26.9569);       // Vathy, Samos
            _municipalityCoordinates["ΔΗΜΟΣ ΑΙΚΑΡΙΑΣ"] = (37.8586, 26.3900);     // Agios Kirykos, Ikaria
            _municipalityCoordinates["ΔΗΜΟΣ ΜΥΡΙΝΑΣ"] = (39.9667, 25.9167);      // Myrina, Lemnos
            _municipalityCoordinates["ΔΗΜΟΣ ΛΗΜΝΟΥ"] = (39.9167, 25.2333);
            _municipalityCoordinates["ΔΗΜΟΣ ΣΑΜΟΥ"] = (37.7547, 26.9769);

            // Aegean Sea municipalities (Cyclades)
            _municipalityCoordinates["ΔΗΜΟΣ ΜΥΚΟΝΟΥ"] = (37.4468, 25.3289);     // Mykonos
            _municipalityCoordinates["ΔΗΜΟΣ ΝΑΞΟΥ"] = (37.1050, 25.3767);        // Naxos (Naxos Town)
            _municipalityCoordinates["ΔΗΜΟΣ ΣΑΝΤΟΡΙΝΗΣ"] = (36.4100, 25.4350);   // Santorini (Fira)
            _municipalityCoordinates["ΔΗΜΟΣ ΠΑΡΟΥ"] = (37.0833, 25.1500);        // Paros (Parikia)
            _municipalityCoordinates["ΔΗΜΟΣ ΣΥΡΟΥ"] = (37.5150, 24.9025);         // Syros (Ermoupoli)
            _municipalityCoordinates["ΔΗΜΟΣ ΤΗΝΟΥ"] = (37.5000, 25.6000);         // Tinos

            // Aegean Sea municipalities (Dodecanese)
            _municipalityCoordinates["ΔΗΜΟΣ ΡΟΔΟΥ"] = (36.4340, 28.2176);         // Rhodes
            _municipalityCoordinates["ΔΗΜΟΣ ΚΩ"] = (36.8941, 27.2869);            // Kos
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΛΥΜΝΟΥ"] = (36.9240, 26.9720);      // Kalymnos
            _municipalityCoordinates["ΔΗΜΟΣ ΛΕΡΟΥ"] = (37.0758, 27.1826);          // Leros
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΡΠΑΘΟΥ"] = (35.4760, 27.1480);       // Karpathos

            // Aegean Sea municipalities (Saronic Gulf - part of Attica)
            _municipalityCoordinates["ΔΗΜΟΣ ΑΙΓΑΙΝΑΣ"] = (37.7500, 23.4300);      // Aegina
            _municipalityCoordinates["ΔΗΜΟΣ ΠΟΡΟΥ"] = (37.5325, 23.6175);         // Poros

            // North Greece - Central Macedonia
            _municipalityCoordinates["ΔΗΜΟΣ ΒΕΡΩΝ"] = (40.5230, 22.1990);   // Veria
            _municipalityCoordinates["ΔΗΜΟΣ ΚΙΛΚΙΣ"] = (40.9400, 22.8800);   // Kilkis
            _municipalityCoordinates["ΔΗΜΟΣ ΣΕΡΡΩΝ"] = (41.0873, 23.5471);   // Serres
            _municipalityCoordinates["ΔΗΜΟΣ ΝΑΟΥΣΑΣ"] = (40.6333, 22.0167);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΤΕΡΙΝΗΣ"] = (40.2680, 22.5020);

            // North Greece - Western Macedonia
            _municipalityCoordinates["ΔΗΜΟΣ ΚΟΖΑΝΗΣ"] = (40.3000, 21.7850);    // Kozani
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΣΤΟΡΙΑΣ"] = (40.5183, 21.2600);   // Kastoria
            _municipalityCoordinates["ΔΗΜΟΣ ΦΛΩΡΙΝΑΣ"] = (40.7811, 21.4069);     // Florina
            _municipalityCoordinates["ΔΗΜΟΣ ΓΡΕΒΕΝΩΝ"] = (40.1861, 21.4097);      // Grevena
            _municipalityCoordinates["ΔΗΜΟΣ ΠΤΟΛΕΜΑΪΔΑΣ"] = (40.6081, 21.5900);   // Ptolemaida
            _municipalityCoordinates["ΔΗΜΟΣ ΣΙΑΤΙΣΤΑΣ"] = (40.2833, 21.6333);
            _municipalityCoordinates["ΔΗΜΟΣ ΝΕΣΤΟΥ"] = (40.7500, 21.6667);

            // North Greece - Eastern Macedonia and Thrace
            _municipalityCoordinates["ΔΗΜΟΣ ΑΛΕΞΑΝΔΡΟΥΠΟΛΗΣ"] = (40.8457, 25.8739); // Alexandroupoli
            _municipalityCoordinates["ΔΗΜΟΣ ΚΟΜΟΤΗΝΗΣ"] = (41.1224, 25.4066);       // Komotini
            _municipalityCoordinates["ΔΗΜΟΣ ΞΑΝΘΗΣ"] = (41.1517, 24.8822);          // Xanthi
            _municipalityCoordinates["ΔΗΜΟΣ ΔΡΑΜΑΣ"] = (41.1500, 24.1500);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΒΑΛΑΣ"] = (40.9369, 24.4067);

            // Ionian Islands municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΚΕΡΚΥΡΑΣ"] = (39.6243, 19.9217);   // Corfu
            _municipalityCoordinates["ΔΗΜΟΣ ΚΕΦΑΛΑΙΝΑΣ"] = (38.1750, 20.6000);   // Kefalonia
            _municipalityCoordinates["ΔΗΜΟΣ ΖΑΚΥΝΘΟΥ"] = (37.8000, 20.7800);   // Zakynthos
            _municipalityCoordinates["ΔΗΜΟΣ ΛΕΥΚΑΔΑΣ"] = (38.7667, 20.7333);   // Lefkada
            _municipalityCoordinates["ΔΗΜΟΣ ΙΘΑΚΗΣ"] = (38.3167, 20.7833);   // Ithaca

            // Cyclades municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΜΥΚΟΝΟΥ"] = (37.4468, 25.3289);   // Mykonos
            _municipalityCoordinates["ΔΗΜΟΣ ΝΑΞΟΥ"] = (37.1050, 25.3767);   // Naxos (Chora)
            _municipalityCoordinates["ΔΗΜΟΣ ΣΑΝΤΟΡΙΝΗΣ"] = (36.4100, 25.4350);   // Santorini (Fira)
            _municipalityCoordinates["ΔΗΜΟΣ ΠΑΡΟΥ"] = (37.0833, 25.1500);   // Paros (Parikia)
            _municipalityCoordinates["ΔΗΜΟΣ ΣΥΡΟΥ"] = (37.5150, 24.9025);   // Syros (Ermoupoli)
            _municipalityCoordinates["ΔΗΜΟΣ ΤΗΝΟΥ"] = (37.5000, 25.6000);   // Tinos
            _municipalityCoordinates["ΔΗΜΟΣ ΙΟΥ"] = (36.7500, 25.4500);   // Ios (approx.)

            // Dodecanese municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΡΟΔΟΥ"] = (36.4340, 28.2176);   // Rhodes
            _municipalityCoordinates["ΔΗΜΟΣ ΚΩ"] = (36.8941, 27.2869);   // Kos
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΛΥΜΝΟΥ"] = (36.9240, 26.9720);   // Kalymnos
            _municipalityCoordinates["ΔΗΜΟΣ ΛΕΡΟΥ"] = (37.0758, 27.1826);   // Leros
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΡΠΑΘΟΥ"] = (35.4760, 27.1480);   // Karpathos
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΣΤΕΛΛΟΡΙΖΟΥ"] = (36.1378, 29.5583);   // Kastellorizo
            _municipalityCoordinates["ΔΗΜΟΣ ΠΑΤΜΟΥ"] = (37.3056, 26.5392);
            _municipalityCoordinates["ΔΗΜΟΣ ΝΙΣΥΡΟΥ"] = (36.5667, 27.2167);
            _municipalityCoordinates["ΔΗΜΟΣ ΚΑΣΟΥ"] = (35.4200, 26.8667);
            _municipalityCoordinates["ΔΗΜΟΣ ΣΥΜΗΣ"] = (36.5767, 27.9350);

            // Sporades municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΣΚΙΑΘΟΥ"] = (39.1667, 23.4833);   // Skiathos
            _municipalityCoordinates["ΔΗΜΟΣ ΣΚΟΠΕΛΟΥ"] = (39.1833, 22.8500);   // Skopelos
            _municipalityCoordinates["ΔΗΜΟΣ ΑΛΟΝΝΙΣΟΥ"] = (39.2000, 23.2500);   // Alonnisos
            _municipalityCoordinates["ΔΗΜΟΣ ΣΚΥΡΟΥ"] = (38.9750, 24.0917);   // Skyros

            // Evia (Εύβοια) municipalities
            _municipalityCoordinates["ΔΗΜΟΣ ΧΑΛΚΙΔΑΣ"] = (38.4639, 23.6067);                  // Chalcis – the administrative capital of Evia
            _municipalityCoordinates["ΔΗΜΟΣ ΚΥΜΗΣ-ΑΛΙΒΕΡΙ"] = (38.4167, 24.0833);               // Kymi-Aliveri municipality (east-central Evia)
            _municipalityCoordinates["ΔΗΜΟΣ ΕΡΕΤΡΙΑΣ"] = (38.5500, 23.9000);                    // Eretria municipality (northeast Evia)
            _municipalityCoordinates["ΔΗΜΟΣ ΔΙΡΦΥΣ-ΜΕΣΣΑΠΙΑΣ"] = (38.1333, 23.7500);           // Dirfys-Messapia municipality (southern Evia)
            _municipalityCoordinates["ΔΗΜΟΣ ΙΣΤΙΑΣ-ΑΙΔΙΨΟΥ"] = (38.2167, 23.4500);              // Istiaia-Aidipsos municipality (northwest Evia)
            _municipalityCoordinates["ΔΗΜΟΣ ΜΑΝΤΟΥΔΙ-ΛΙΜΝΙΟΥ-ΑΓΙΑΣ ΑΝΝΑΣ"] = (38.1667, 23.6833); // Mantoudi-Limni-Agia Anna municipality (central-west Evia)


            // Others (I add here cases where no match is found)
            _municipalityCoordinates["ΔΗΜΟΣ ΠΑΞΩΝ"] = (39.2167, 20.2167);

            // Epirus
            _municipalityCoordinates["ΔΗΜΟΣ ΒΟΒΟΥΣΗΣ"] = (39.7500, 20.7167); // approximate
            _municipalityCoordinates["ΔΗΜΟΣ ΠΡΕΒΕΖΑΣ"] = (38.9597, 20.7517); // already partially exists as ΔΗΜΟΣ ΠΡΕΒΕΖΗΣ

            // West Greece
            _municipalityCoordinates["ΔΗΜΟΣ ΓΑΣΤΟΥΝΗΣ"] = (37.9167, 21.3833); // Gastouni
            _municipalityCoordinates["ΔΗΜΟΣ ΤΡΑΓΑΝΟΥ"] = (37.8500, 21.4500); // Traganou
            _municipalityCoordinates["ΔΗΜΟΣ ΙΕΡΑΣ ΠΟΛΗΣ ΜΕΣΟΛΟΓΓΙΟΥ - ΑΙΤΩΛΙΚΟΥ"] = (38.3750, 21.3167); // Messolonghi-Aitoliko

            // North Aegean
            _municipalityCoordinates["ΔΗΜΟΣ ΧΙΟΥ - ΑΜΑΝΗΣ"] = (38.3000, 25.0667); // Amanis, Chios

            _municipalityCoordinates["ΔΗΜΟΣ ΩΡΩΠΟΥ - ΑΥΛΩΝΟΣ"] = (38.2167, 23.8333); // Oropos - Avlonas, Attica
            // Add more as needed

            // keys for region-municipality combinations
            var compositeEntries = new Dictionary<string, (double Lat, double Lon)>();
            foreach (var region in _regionCoordinates)
            {
                foreach (var municipality in _municipalityCoordinates)
                {
                    string compositeKey = $"{region.Key}-{municipality.Key}";
                    compositeEntries[compositeKey] = municipality.Value;
                }
            }

            foreach (var entry in compositeEntries)
            {
                _municipalityCoordinates[entry.Key] = entry.Value;
            }
        }

        // Save new coordinates for future geocoding
        private void SaveNewCoordinates(string key, double lat, double lon)
        {
            if (string.IsNullOrEmpty(key) || lat == 0 || lon == 0)
                return;

            try
            {
                if (!_municipalityCoordinates.ContainsKey(key))
                {
                    _municipalityCoordinates[key] = (lat, lon);
                    _logger.LogInformation($"Added new coordinates for '{key}': {lat}, {lon}");

                    var directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    var filePath = Path.Combine(directoryPath, "municipalities.json");

                    Dictionary<string, (double Lat, double Lon)> data = new Dictionary<string, (double Lat, double Lon)>();
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            var json = File.ReadAllText(filePath, Encoding.UTF8);
                            var existingData = JsonSerializer.Deserialize<Dictionary<string, (double Lat, double Lon)>>(json);
                            if (existingData != null)
                            {
                                data = existingData;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error reading existing municipality data file");
                        }
                    }

                    data[key] = (lat, lon);

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var newJson = JsonSerializer.Serialize(data, options);
                    File.WriteAllText(filePath, newJson, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving new coordinates for '{key}'");
            }
        }

        public async Task<GeocodedIncident> GeocodeIncidentAsync(FireIncident incident)
        {
            var geocodedIncident = new GeocodedIncident
            {
                Status = incident.Status,
                Category = incident.Category,
                Region = incident.Region,
                Municipality = incident.Municipality,
                Location = incident.Location,
                StartDate = incident.StartDate,
                LastUpdate = incident.LastUpdate
            };

            try
            {
                _logger.LogInformation("Geocoding incident: {Region}, {Municipality}, {Location}",
                    incident.Region, incident.Municipality, incident.Location);

                string locationKey = GetLocationKey(incident);

                string compositeKey = null;
                string municipalityKey = null;
                string regionKey = null;

                if (!string.IsNullOrEmpty(incident.Region) && !string.IsNullOrEmpty(incident.Municipality))
                {
                    compositeKey = $"{incident.Region}-{incident.Municipality}";
                }

                if (!string.IsNullOrEmpty(incident.Municipality))
                {
                    municipalityKey = incident.Municipality;
                }

                if (!string.IsNullOrEmpty(incident.Region))
                {
                    regionKey = incident.Region;
                }

                // Check for cached coordinates for this location
                string cacheKey = GetCacheKey(incident.Region, incident.Municipality, incident.Location);

                double lat = 0;
                double lon = 0;
                bool foundCoordinates = false;
                string sourceDescription = "Unknown";

                if (_cache.TryGetValue(cacheKey, out (double Lat, double Lon) coordinates))
                {
                    _logger.LogInformation("Found cached coordinates for {CacheKey}: {Lat}, {Lon}",
                        cacheKey, coordinates.Lat, coordinates.Lon);
                    lat = coordinates.Lat;
                    lon = coordinates.Lon;
                    foundCoordinates = true;
                    sourceDescription = "Cache";
                }
                else
                {
                    // FIRST TRY: geocode with OSM (active searching)
                    string searchAddress = GetPreciseSearchAddress(incident);
                    _logger.LogInformation("Attempting active search with: {SearchAddress}", searchAddress);

                    try
                    {
                        var (geocodedLat, geocodedLon) = await GeocodeAddressAsync(searchAddress);

                        if (geocodedLat != 0 && geocodedLon != 0)
                        {
                            _logger.LogInformation("Successfully geocoded to: {Lat}, {Lon}",
                                geocodedLat, geocodedLon);
                            lat = geocodedLat;
                            lon = geocodedLon;
                            foundCoordinates = true;
                            sourceDescription = "Nominatim geocoding";

                            // Cache coordinates
                            _cache.Set(cacheKey, (lat, lon), TimeSpan.FromDays(30));

                            // save to our coordinates dictionary for future use
                            if (municipalityKey != null)
                            {
                                SaveNewCoordinates(municipalityKey, lat, lon);
                            }
                            if (compositeKey != null)
                            {
                                SaveNewCoordinates(compositeKey, lat, lon);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Active search failed for: {SearchAddress}. Falling back to predefined coordinates.",
                                searchAddress);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during active geocoding. Falling back to predefined coordinates.");
                    }

                    // SECOND TRY: If active search failed, try predefined coordinates
                    if (!foundCoordinates)
                    {
                        // Try looking to region-municipality
                        if (compositeKey != null && _municipalityCoordinates.TryGetValue(compositeKey, out var compositeCoords))
                        {
                            _logger.LogInformation("Using predefined coordinates for composite key '{CompositeKey}': {Lat}, {Lon}",
                                compositeKey, compositeCoords.Lat, compositeCoords.Lon);
                            lat = compositeCoords.Lat;
                            lon = compositeCoords.Lon;
                            foundCoordinates = true;
                            sourceDescription = $"Predefined composite key: {compositeKey}";

                            _cache.Set(cacheKey, (lat, lon), TimeSpan.FromDays(30));
                        }
                        // Try looking just the municipality
                        else if (municipalityKey != null && _municipalityCoordinates.TryGetValue(municipalityKey, out var municipalityCoords))
                        {
                            _logger.LogInformation("Using predefined coordinates for municipality '{Municipality}': {Lat}, {Lon}",
                                municipalityKey, municipalityCoords.Lat, municipalityCoords.Lon);
                            lat = municipalityCoords.Lat;
                            lon = municipalityCoords.Lon;
                            foundCoordinates = true;
                            sourceDescription = $"Predefined municipality: {municipalityKey}";

                            _cache.Set(cacheKey, (lat, lon), TimeSpan.FromDays(30));
                        }
                        // Try with predefined region coordinates
                        else if (regionKey != null && _regionCoordinates.TryGetValue(regionKey, out var regionCoords))
                        {
                            _logger.LogInformation("Using predefined coordinates for region '{Region}': {Lat}, {Lon}",
                                regionKey, regionCoords.Lat, regionCoords.Lon);
                            lat = regionCoords.Lat;
                            lon = regionCoords.Lon;
                            foundCoordinates = true;
                            sourceDescription = $"Predefined region: {regionKey}";

                            _cache.Set(cacheKey, (lat, lon), TimeSpan.FromDays(30));
                        }
                        else
                        {
                            // If nothing else worked, use default coordinates
                            _logger.LogWarning("No coordinates found. Using default coordinates for Greece: {Lat}, {Lon}",
                                _defaultLat, _defaultLon);
                            lat = _defaultLat;
                            lon = _defaultLon;
                            foundCoordinates = true;
                            sourceDescription = "Default Greece coordinates";
                        }
                    }
                }

                // Offset the coordinates if there are other incidents on the same municipality
                if (foundCoordinates)
                {
                    var originalCoords = (lat, lon);
                    (lat, lon) = GetOffsetCoordinates(locationKey, lat, lon);

                    if (originalCoords.lat != lat || originalCoords.lon != lon)
                    {
                        _logger.LogDebug("Offset coordinates from ({OrigLat}, {OrigLon}) to ({NewLat}, {NewLon}) to avoid overlap",
                            originalCoords.lat, originalCoords.lon, lat, lon); //log if an incident gets offset coordinates
                    }
                }

                geocodedIncident.Latitude = lat;
                geocodedIncident.Longitude = lon;
                //geocodedIncident.IsGeocoded = true;
                geocodedIncident.GeocodingSource = sourceDescription;

                return geocodedIncident;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error geocoding incident: {Region}, {Municipality}, {Location}",
                    incident.Region, incident.Municipality, incident.Location);

                geocodedIncident.Latitude = _defaultLat;
                geocodedIncident.Longitude = _defaultLon;
                //geocodedIncident.IsGeocoded = false;
                geocodedIncident.GeocodingSource = "Error fallback";

                return geocodedIncident;
            }
        }

        private string GetPreciseSearchAddress(FireIncident incident)
        {
            // StringBuilder to properly handle UTF-8 string
            var address = new StringBuilder();

            try
            {
                // terms that should be excluded from search (incident types and generic locations)
                HashSet<string> excludedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ΥΠΑΙΘΡΟΣ", "ΑΛΛΗ ΠΕΡΙΠΤΩΣΗ", "ΚΤΙΡΙΟ ΚΑΤΟΙΚΙΑΣ", "ΧΩΡΟΣ ΑΠΟΘΗΚΕΥΣΗΣ", "ΧΩΡΟΣ ΕΜΠΟΡΙΟΥ", "ΧΩΡΟΣ ΣΥΝΑΘΡΟΙΣΗΣ ΚΟΙΝΟΥ",
                    "ΔΑΣΟΣ", "ΕΚΤΑΣΗ", "ΔΑΣΙΚΗ ΕΚΤΑΣΗ", "ΓΕΩΡΓΙΚΗ ΕΚΤΑΣΗ", "ΧΟΡΤΟΛΙΒΑΔΙΚΗ ΕΚΤΑΣΗ", "ΥΠΟΛΕΙΜΜΑΤΑ ΚΑΛΛΙΕΡΓΕΙΩΝ",
            
                    "ΤΡΟΧΑΙΟ", "ΠΥΡΚΑΓΙΑ", "ΦΩΤΙΑ", "ΠΥΡΚΑΪΑ", "ΕΓΚΛΩΒΙΣΜΟΣ", "ΔΙΑΣΩΣΗ ΖΩΟΥ", "ΑΝΤΛΗΣΗ", "ΑΦΑΙΡΕΣΗ ΑΝΤΙΚΕΙΜΕΝΟΥ", "ΚΑΤΑΠΛΑΚΩΣΗ",
                    "ΒΙΟΜΗΧΑΝΙΑ – ΒΙΟΤΕΧΝΙΑ", "ΒΙΟΜΗΧΑΝΙΑ", "ΒΙΟΤΕΧΝΙΑ",
                    "ΚΑΛΑΜΙΑ", "ΒΑΛΤΟΙ", "ΚΑΛΑΜΙΑ - ΒΑΛΤΟΙ", "ΚΟΠΗ ΔΕΝΔΡΟΥ", "ΚΟΠΗ", "ΠΛΥΣΗ", "ΜΕΤΑΦΟΡΙΚΑ ΜΕΣΑ",

            
                    "ΚΤΙΡΙΟ", "ΚΤΙΡΙΟ ΥΓΕΙΑΣ", "ΚΤΙΡΙΟ ΥΓΕΙΑΣ ΚΑΙ ΚΟΙΝΩΝΙΚΗΣ ΠΡΟΝΟΙΑΣ",
                    "ΑΛΛΗ", "ΑΛΛΟ", "ΑΛΛΗΣ", "ΑΛΛΟΥ"
                };

                bool locationIsGeographical = false;

                if (!string.IsNullOrEmpty(incident.Location))
                {
                    locationIsGeographical = !excludedTerms.Contains(incident.Location) &&
                                            !excludedTerms.Any(term => incident.Location.Contains(term, StringComparison.OrdinalIgnoreCase));

                    if (locationIsGeographical)
                    {
                        address.Append(incident.Location);
                        _logger.LogDebug("Using specific location: {Location}", incident.Location);
                    }
                    else
                    {
                        _logger.LogDebug("Skipping non-geographical location: {Location}", incident.Location);
                    }
                }

                if (!string.IsNullOrEmpty(incident.Municipality))
                {
                    if (address.Length > 0) address.Append(", ");

                    address.Append(incident.Municipality);
                    _logger.LogDebug("Using complete municipality: {Municipality}", incident.Municipality);
                }

                if (!string.IsNullOrEmpty(incident.Region))
                {
                    if (address.Length > 0) address.Append(", ");

                    string region = incident.Region;
                    if (region.StartsWith("ΠΕΡΙΦΕΡΕΙΑ ", StringComparison.OrdinalIgnoreCase)) //Clean up
                    {
                        region = region.Substring("ΠΕΡΙΦΕΡΕΙΑ ".Length);
                    }

                    address.Append(region);
                    _logger.LogDebug("Using region: {Region}", region);
                }

                if (address.Length > 0) address.Append(", ");
                address.Append("Greece");

                string result = address.ToString();
                byte[] addressBytes = Encoding.UTF8.GetBytes(result);
                string addressUTF8 = Encoding.UTF8.GetString(addressBytes);

                _logger.LogDebug("Built search address: {Address}", addressUTF8);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building search address");
                return "Greece";
            }
        }

        // Get a unique key for the incident location
        private string GetLocationKey(FireIncident incident)
        {
            if (!string.IsNullOrEmpty(incident.Municipality) && !string.IsNullOrEmpty(incident.Region))
            {
                return $"{incident.Region}-{incident.Municipality}".ToLowerInvariant();
            }

            if (!string.IsNullOrEmpty(incident.Region))
            {
                return incident.Region.ToLowerInvariant();
            }

            return "unknown";
        }

        // Get offset coordinates to avoid overlapping markers
        private (double Lat, double Lon) GetOffsetCoordinates(string locationKey, double lat, double lon)
        {
            if (!_activeIncidentCoordinates.ContainsKey(locationKey))
            {
                _activeIncidentCoordinates[locationKey] = new List<(double Lat, double Lon)>();
            }

            var existingCoordinates = _activeIncidentCoordinates[locationKey];

            if (existingCoordinates.Count == 0)
            {
                existingCoordinates.Add((lat, lon));
                return (lat, lon);
            }

            double offsetDistance = 0.01 * (existingCoordinates.Count); // about 1km per incident
            double angle = Math.PI * 2 * existingCoordinates.Count / 8; // Distribute in a circle

            double offsetLat = lat + offsetDistance * Math.Sin(angle);
            double offsetLon = lon + offsetDistance * Math.Cos(angle);

            existingCoordinates.Add((offsetLat, offsetLon));

            return (offsetLat, offsetLon);
        }

        private string GetCacheKey(string region, string municipality, string location)
        {
            string sanitized = $"{SanitizeForCacheKey(region)}_{SanitizeForCacheKey(municipality)}_{SanitizeForCacheKey(location)}";
            return $"geocode_{sanitized}";
        }

        private string SanitizeForCacheKey(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "unknown";

            // Remove special characters and trim
            return Regex.Replace(input, @"[^\w\s]", "").Trim().Replace(" ", "_").ToLowerInvariant();
        }

        private async Task<(double Lat, double Lon)> GeocodeAddressAsync(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                _logger.LogWarning("Empty address provided for geocoding");
                return (0, 0);
            }

            try
            {
                // Log with Unicode handling
                byte[] addressBytes = Encoding.UTF8.GetBytes(address);
                string addressUTF8 = Encoding.UTF8.GetString(addressBytes);
                _logger.LogInformation("Geocoding address: {Address}", addressUTF8);

                List<string> searchVariations = GenerateSearchVariations(address);

                foreach (var searchAddress in searchVariations)
                {
                    // Delay for Nominatim since it has rate limits
                    await Task.Delay(1000);

                    if (searchAddress != address)
                    {
                        _logger.LogDebug("Trying alternative search: {AlternativeAddress}", searchAddress);
                    }

                    // Create a new HttpClient for each request to avoid header issues
                    using (var client = new HttpClient())
                    {
                        // Configure timeout
                        client.Timeout = TimeSpan.FromSeconds(30);

                        // Add required headers for Nominatim with explicit encoding information
                        client.DefaultRequestHeaders.Add("User-Agent", "FireIncidentsMapApplication/1.0");
                        client.DefaultRequestHeaders.Add("Accept", "application/json; charset=utf-8");
                        client.DefaultRequestHeaders.Add("Accept-Charset", "utf-8");

                        // Properly encode the address for URL using UTF-8
                        var encodedAddress = Uri.EscapeDataString(searchAddress);

                        // Enhanced parameters for better results:
                        var requestUrl = $"{_nominatimBaseUrl}?q={encodedAddress}&format=json&limit=1&accept-language=el&addressdetails=1&countrycodes=gr";

                        // Add viewbox parameter to limit to Greece
                        requestUrl += "&viewbox=19.22,34.72,29.64,41.75&bounded=1";

                        _logger.LogDebug("Geocoding request URL: {Url}", requestUrl);

                        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                        request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
                        request.Headers.Add("Accept-Language", "el,en;q=0.9");

                        var response = await client.SendAsync(request);

                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.LogWarning("Geocoding request failed with status code {StatusCode}", response.StatusCode);
                            continue; // Try next variation
                        }

                        // Explicitly get content as bytes and convert to string with UTF-8 encoding
                        var contentBytes = await response.Content.ReadAsByteArrayAsync();
                        var content = Encoding.UTF8.GetString(contentBytes);

                        // Log with Unicode handling
                        _logger.LogDebug("Geocoding response: {Response}", content);

                        // Check if we got an empty array
                        if (content == "[]" || string.IsNullOrWhiteSpace(content))
                        {
                            _logger.LogWarning("Geocoding returned no results for address: {Address}", searchAddress);
                            continue; // Try next variation
                        }

                        try
                        {
                            var results = JsonSerializer.Deserialize<JsonElement[]>(content);

                            if (results != null && results.Length > 0)
                            {
                                var result = results[0];

                                // Also log the type of place found
                                string placeType = "unknown";
                                if (result.TryGetProperty("type", out JsonElement typeElement))
                                {
                                    placeType = typeElement.GetString() ?? "unknown";
                                }

                                string displayName = "unknown";
                                if (result.TryGetProperty("display_name", out JsonElement displayElement))
                                {
                                    displayName = displayElement.GetString() ?? "unknown";
                                }

                                // Log with Unicode handling
                                byte[] displayBytes = Encoding.UTF8.GetBytes(displayName);
                                string displayUTF8 = Encoding.UTF8.GetString(displayBytes);
                                _logger.LogInformation("Found place type: {PlaceType}, name: {DisplayName}", placeType, displayUTF8);

                                if (result.TryGetProperty("lat", out JsonElement latElement) &&
                                    result.TryGetProperty("lon", out JsonElement lonElement))
                                {
                                    // Handle different ways lat/lon might be represented
                                    if (latElement.ValueKind == JsonValueKind.String &&
                                        lonElement.ValueKind == JsonValueKind.String)
                                    {
                                        if (double.TryParse(latElement.GetString(),
                                            NumberStyles.Float,
                                            CultureInfo.InvariantCulture,
                                            out double lat) &&
                                            double.TryParse(lonElement.GetString(),
                                            NumberStyles.Float,
                                            CultureInfo.InvariantCulture,
                                            out double lon))
                                        {
                                            _logger.LogInformation("Successfully geocoded with variation \"{Variation}\": {Lat}, {Lon}",
                                                searchAddress, lat, lon);
                                            return (lat, lon);
                                        }
                                    }
                                    else if (latElement.ValueKind == JsonValueKind.Number &&
                                            lonElement.ValueKind == JsonValueKind.Number)
                                    {
                                        double lat = latElement.GetDouble();
                                        double lon = lonElement.GetDouble();
                                        _logger.LogInformation("Successfully geocoded with variation \"{Variation}\": {Lat}, {Lon}",
                                            searchAddress, lat, lon);
                                        return (lat, lon);
                                    }
                                }
                            }

                            _logger.LogWarning("Could not extract coordinates from geocoding result for address: {Address}", searchAddress);
                            // Continue to next variation
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "Error parsing geocoding JSON for address: {Address}", searchAddress);
                            _logger.LogDebug("Raw JSON: {Content}", content);
                            // Continue to next variation
                        }
                    }
                }

                // If we get here, all variations failed
                _logger.LogWarning("All geocoding attempts failed for: {Address}", addressUTF8);
                return (0, 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error geocoding address: {Address}", address);
                return (0, 0);
            }
        }

        private List<string> GenerateSearchVariations(string address)
        {
            List<string> variations = new List<string>();

            // Add the original address as the first option
            variations.Add(address);

            try
            {
                // Extract components from the address
                // Typical format: "Δ. MUNICIPALITY - SPECIFIC_LOCATION, REGION, Greece"
                string region = "Greece";
                string municipality = "";
                string specificLocation = "";

                // First, split by ", " to separate the main parts
                string[] mainParts = address.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

                if (mainParts.Length >= 3)
                {
                    // Extract the region (usually the second-to-last part)
                    region = mainParts[mainParts.Length - 2];

                    // Extract the municipality part (first part)
                    string municipalityPart = mainParts[0];

                    // Check if it has a hyphen separator 
                    if (municipalityPart.Contains(" - "))
                    {
                        string[] municipalityParts = municipalityPart.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);

                        if (municipalityParts.Length >= 2)
                        {
                            // First part is the municipality
                            municipality = municipalityParts[0];

                            // Second part is the specific location
                            specificLocation = municipalityParts[1];

                            // Clean up municipality from the "Δ. " prefix if present
                            if (municipality.StartsWith("Δ. ", StringComparison.OrdinalIgnoreCase))
                            {
                                municipality = municipality.Substring(3);
                            }

                            // Add variations based on parsed components

                            // 1.  specific location with region
                            variations.Add($"{specificLocation}, {region}, Greece");

                            // 2. municipality without the specific location or "Δ." prefix
                            variations.Add($"{municipality}, {region}, Greece");

                            // 3. both parts without the hyphen and without "Δ." prefix
                            variations.Add($"{municipality} {specificLocation}, {region}, Greece");

                            // 4. specific location first, then municipality
                            variations.Add($"{specificLocation}, {municipality}, {region}, Greece");
                        }
                    }
                    else
                    {
                        // No hyphen, just try without the "Δ." prefix if present
                        if (municipalityPart.StartsWith("Δ. ", StringComparison.OrdinalIgnoreCase))
                        {
                            variations.Add($"{municipalityPart.Substring(3)}, {region}, Greece");
                        }
                    }
                }
                else if (mainParts.Length == 2)
                {
                    // Format might be "MUNICIPALITY, REGION, Greece"
                    string municipalityPart = mainParts[0];
                    region = mainParts[1];

                    // Just try without the "Δ." prefix if present
                    if (municipalityPart.StartsWith("Δ. ", StringComparison.OrdinalIgnoreCase))
                    {
                        variations.Add($"{municipalityPart.Substring(3)}, {region}, Greece");
                    }
                }

                // If there's a "Δ." in the address anywhere, try a version without it
                if (address.Contains("Δ."))
                {
                    variations.Add(address.Replace("Δ. ", ""));
                }

                // If there's a hyphen anywhere, try a version with space instead
                if (address.Contains(" - "))
                {
                    variations.Add(address.Replace(" - ", " "));
                }

                // Try just with the region name
                if (!string.IsNullOrEmpty(region) && region != "Greece")
                {
                    variations.Add($"{region}, Greece");
                }

                // Make the variations unique (remove duplicates)
                variations = variations.Distinct().ToList();

                _logger.LogDebug("Generated {Count} search variations for: {Address}", variations.Count, address);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating search variations for: {Address}", address);
                // Return just the original address if there's an error
                return new List<string> { address };
            }

            return variations;
        }
    }
}