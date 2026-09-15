using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using RescuAR.App.Services.Prepare;

namespace RescuAR.App.ViewModels.Prepare;

public class ProtocolPageItem
{
    public int PageNumber { get; set; }
    public string SectionLetter { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string ImageSource { get; set; } = string.Empty;
    public string HeaderBgColor { get; set; } = "#0A8491";
    public string TagColor { get; set; } = "#065F69";
    public string IconData { get; set; } = string.Empty;
    public ObservableCollection<string> Highlights { get; set; } = new();
    public string DetailedContent { get; set; } = string.Empty;
}

public partial class PreparednessGuideViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Marikina City Disaster Protocol";

    [ObservableProperty]
    private string _subtitle = "Official DRRMO Rescue 161 Standard Operating Guidelines";

    [ObservableProperty]
    private int _selectedPageIndex = 0;

    [ObservableProperty]
    private ProtocolPageItem? _currentPage;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _downloadStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isPdfModalVisible;

    [ObservableProperty]
    private bool _isLoadingPdfPages;

    public ObservableCollection<ProtocolPageItem> ProtocolPages { get; } = new();

    public ObservableCollection<ImageSource> PdfPageImages { get; } = new();

    public PreparednessGuideViewModel()
    {
        InitializeProtocolPages();
        if (ProtocolPages.Count > 0)
        {
            CurrentPage = ProtocolPages[0];
        }
    }

    private void InitializeProtocolPages()
    {
        ProtocolPages.Clear();

        // Page 1: Flood Protocol
        ProtocolPages.Add(new ProtocolPageItem
        {
            PageNumber = 1,
            SectionLetter = "A",
            Title = "FLOOD PROTOCOL",
            Subtitle = "Marikina River Water Level Monitoring & Alarm Sirens",
            ImageSource = "marikina_protocol_p1.jpg",
            HeaderBgColor = "#0A8491",
            TagColor = "#0284C7",
            IconData = "M12,20C15.87,20 19,16.87 19,13C19,8.5 12,2.5 12,2.5C12,2.5 5,8.5 5,13C5,16.87 8.13,20 12,20Z",
            Highlights = new ObservableCollection<string>
            {
                "STEP 1: Continuous monitoring of river water level, rainfall & flooded areas.",
                "STEP 2: Class cancellation & public info dissemination via Rescue 161 & PDO.",
                "STEP 3: Activation of Camp Management System at 14.0 Meters river level.",
                "STEP 4: Siren Alert Thresholds: 15m (1st Alarm - Standby), 16m (2nd Alarm - Prepare), 18m (3rd Alarm - Mandatory Evacuation).",
                "STEP 5: Deployment of Rescue 161, PNP, BFP, OPSS, Medical & Transport teams."
            },
            DetailedContent = "REPUBLIC OF THE PHILIPPINES\nCITY GOVERNMENT OF MARIKINA\nRESCUE 161 - DRRMO MARIKINA\n\nA. FLOOD PROTOCOL\n\nSTEP 1: Monitoring of weather condition, Marikina water level, rain fall density and flooded areas.\nSTEP 2: Dissemination of information & Announcement of Class Cancellation.\nSTEP 3: Activation of Camp Management System at 14.0 Meters.\nSTEP 4: Alarm of Siren:\n  - 15 Meters: 1st Alarm (Standby - All must be alert & prepare to evacuate)\n  - 16 Meters: 2nd Alarm (2 mins continuous siren - All must evacuate on low lying areas)\n  - 18 Meters: 3rd Alarm (5 mins continuous siren - Mandatory evacuation on all low lying areas)\n\nRescue 161 DRRMO Marikina | Tel: 7116 5532"
        });

        // Page 2: Camp Management Protocol
        ProtocolPages.Add(new ProtocolPageItem
        {
            PageNumber = 2,
            SectionLetter = "B",
            Title = "CAMP MANAGEMENT PROTOCOL",
            Subtitle = "Evacuation Center Setup & Demobilization Guidelines",
            ImageSource = "marikina_protocol_p2.jpg",
            HeaderBgColor = "#065F69",
            TagColor = "#0369A1",
            IconData = "M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z",
            Highlights = new ObservableCollection<string>
            {
                "ACTIVATION AT 14.5 METERS MARIKINA RIVER WATER LEVEL.",
                "Step 1: City Govt & Principal Camp Managers activate teams & open designated schools.",
                "Step 2: Check-in of managers & staff with Rescue 161-DRRMO Marikina.",
                "Step 3: Briefing & positioning of sector tasking (Food, Sanitation, Security, Medical).",
                "Step 4: Set-up of modular tents & registration desks.",
                "Step 5: Standby for arrival of evacuees.",
                "Demobilization upon PAGASA clearing, river level below 14.4m, and NDRRMC advisory."
            },
            DetailedContent = "B. CAMP MANAGEMENT PROTOCOL\nMARIKINA RIVER WATER LEVEL 14.5 METERS\n\n1. Activation of Camp Managers (City Govt + School Principals).\n2. Coordinate with Barangay Coordinator for setup of Covered Court & classrooms.\n3. CampSys Encoder activation.\n4. Demobilization triggered when river drops below 14.4m with zero rainfall reading."
        });

        // Page 3: Earthquake Protocol
        ProtocolPages.Add(new ProtocolPageItem
        {
            PageNumber = 3,
            SectionLetter = "C",
            Title = "EARTHQUAKE PROTOCOL",
            Subtitle = "Duck, Cover & Hold, Incident Command & RDANA",
            ImageSource = "marikina_protocol_p3.jpg",
            HeaderBgColor = "#92400E",
            TagColor = "#B45309",
            IconData = "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1Z",
            Highlights = new ObservableCollection<string>
            {
                "BEFORE: Understand hazard maps, inspect structural integrity, secure heavy furniture, prepare emergency kit.",
                "DURING: Perform DUCK, COVER, AND HOLD immediately. Protect head and stay calm.",
                "AFTER: Incident Command System (ICS) implementation headed by City Mayor as Incident Commander.",
                "Check Incident Command Center integrity, report department damage, deploy RDANA teams.",
                "Search & Rescue (USAR), Medical Triage, Transport Evacuation, Communication & Power Restoration."
            },
            DetailedContent = "C. EARTHQUAKE PROTOCOL\n\nBEFORE: Conduct seismic risk assessment & structural inspections.\nDURING: Perform Duck, Cover and Hold.\nAFTER: Implementation of Incident Command System headed by City Mayor.\nDeployment of RDANA (Rapid Damage Assessment and Needs Analysis) teams."
        });

        // Page 4: Fire Incident Protocol
        ProtocolPages.Add(new ProtocolPageItem
        {
            PageNumber = 4,
            SectionLetter = "D",
            Title = "FIRE INCIDENT PROTOCOL",
            Subtitle = "Emergency Response, Fire Suppression & Recovery",
            ImageSource = "marikina_protocol_p1.jpg",
            HeaderBgColor = "#B91C1C",
            TagColor = "#DC2626",
            IconData = "M12,2C8.13,2 5,5.13 5,9V14H3.5C2.67,14 2,14.67 2,15.5V17C2,17.83 2.67,18.5 3.5,18.5H20.5C21.33,18.5 22,17.83 22,17V15.5C22,14.67 21.33,14 20.5,14H19V9C19,5.13 15.87,2 12,2ZM7,9C7,6.24 9.24,4 12,4C14.76,4 17,6.24 17,9V14H7V9Z",
            Highlights = new ObservableCollection<string>
            {
                "CALLER -> RESCUE 161 (Central Communication Command Center).",
                "DURING RESPONSE: Fire Suppression by Bureau of Fire Protection (BFP) & Fire Volunteers.",
                "Medical Service (EMS) & Red Cross triage.",
                "Traffic & Crowd Control by OPSS, PNP & Barangay Tanod.",
                "AFTER RESPONSE: Camp Management, Relief & Welfare (CSWDO), Sanitation & Clearing (CEMO, RPA)."
            },
            DetailedContent = "D. FIRE INCIDENT PROTOCOL\n\nCaller -> Rescue 161 (Central Command Center) -> Information Dissemination to Mayor & Operations Head.\nDuring Response: BFP Fire Suppression, EMS Rescue 161, OPSS Traffic Control.\nAfter Response: Relief & Modular Tent Shelter, Debris Clearing."
        });

        // Page 5: Mass Casualty Incident Protocol
        ProtocolPages.Add(new ProtocolPageItem
        {
            PageNumber = 5,
            SectionLetter = "E",
            Title = "MASS CASUALTY INCIDENT",
            Subtitle = "Incident Command System (ICS) & Triage Operations",
            ImageSource = "marikina_protocol_p3.jpg",
            HeaderBgColor = "#6B21A8",
            TagColor = "#7E22CE",
            IconData = "M19,3H5C3.9,3 3,3.9 3,5V19C3,20.1 3.9,21 5,21H19C20.1,21 21,20.1 21,19V5C21,3.9 20.1,3 19,3M12,6C13.66,6 15,7.34 15,9C15,10.66 13.66,12 12,12C10.34,12 9,10.66 9,9C9,7.34 10.34,6 12,6ZM18,18H6V16.83C6,14.83 10,13.72 12,13.72C14,13.72 18,14.83 18,16.83V18Z",
            Highlights = new ObservableCollection<string>
            {
                "CALLER -> RESCUE 161 (Central Command Center).",
                "Emergency Medical Service (EMS) Lead Agency: Rescue 161 (DRRMO Marikina).",
                "Initial triaging, collection point setup & immediate hospital transport.",
                "Support: City Health Office, Red Cross, Engineering Dept, BFP.",
                "Crowd Control & Security: PNP, OPSS, Bantay Bayan & Barangay Tanod."
            },
            DetailedContent = "E. MASS CASUALTY INCIDENT PROTOCOL (ICS)\n\nLead Agency: Rescue 161 (DRRMO Marikina).\nTriage area setup, field hospital coordination, and immediate transport of red/yellow tag casualties."
        });

        // Page 6: Call Taking & Dispatch Flow
        ProtocolPages.Add(new ProtocolPageItem
        {
            PageNumber = 6,
            SectionLetter = "F",
            Title = "CALL TAKING PROTOCOL",
            Subtitle = "Rescue 161 (4C) Emergency Hotline Flow Chart",
            ImageSource = "marikina_protocol_p6.jpg",
            HeaderBgColor = "#15803D",
            TagColor = "#166534",
            IconData = "M6.62,10.79C8.06,13.62 10.38,15.94 13.21,17.38L15.41,15.18C15.69,14.9 16.08,14.82 16.43,14.93C17.55,15.3 18.75,15.5 20,15.5A1,1 0 0,1 21,16.5V20A1,1 0 0,1 20,21C10.61,21 3,13.39 3,4A1,1 0 0,1 4,3H7.5A1,1 0 0,1 8.5,4C8.5,5.25 8.7,6.45 9.07,7.57C9.18,7.92 9.1,8.31 8.82,8.59L6.62,10.79Z",
            Highlights = new ObservableCollection<string>
            {
                "HOTLINES: DIAL 161 | Landline: 646-2436 to 38 | SMART: 0928-5593341 | GLOBE: 0917-5842168.",
                "5 ESSENTIAL CALLER QUESTIONS: 1. Nature (What happened?), 2. Exact Location with Landmark, 3. Phone Number, 4. Caller Name, 5. Extent of Injury/Damage.",
                "DISPATCHING: PNP (Police), BFP (Fire), OPSS (Traffic), City Agencies (General Concerns).",
                "DISPATCHER INSTRUCTION TO CALLER: Remain calm, stay on line until ambulance arrives, do not move patient if alone."
            },
            DetailedContent = "F. CALL TAKING PROTOCOL\nRESCUE 161 (4C) ASSISTANCE FLOW CHART\n\nDIAL 161\nLandlines: 646-2436 to 38\nSMART: 0928-5593341 | GLOBE: 0917-5842168\n\nFILTRATION QUESTIONS:\n1. Nature of Emergency\n2. Exact Location & Landmark\n3. Phone Number\n4. Caller Name\n5. Extent of Injury/Damage"
        });

        // Page 7: Patient Transport & COVID Protocol
        ProtocolPages.Add(new ProtocolPageItem
        {
            PageNumber = 7,
            SectionLetter = "G",
            Title = "PATIENT TRANSPORT PROTOCOL",
            Subtitle = "MCESU Endorsement, Hospital Transfer & Decontamination",
            ImageSource = "marikina_protocol_p2.jpg",
            HeaderBgColor = "#0369A1",
            TagColor = "#0284C7",
            IconData = "M19,10.5V7C19,6.45 18.55,6 18,6H4C3.45,6 3,6.45 3,7V17C3,17.55 3.45,18 4,18H18C18.55,18 19,17.55 19,17V13.5L23,17.5V6.5L19,10.5Z",
            Highlights = new ObservableCollection<string>
            {
                "STEP 1: Endorsement from Marikina City Epidemiology Surveillance Unit (MCESU).",
                "STEP 2: Confirmation with Rescue 161 - DRRMO Marikina & Receiving Hospital.",
                "STEP 3: Patient pickup & safe transport by trained decon crew.",
                "STEP 4: Decontamination of ambulance & personnel by BFP Special Rescue Unit (SRU)."
            },
            DetailedContent = "G. COVID & SPECIAL PATIENT TRANSPORT PROTOCOL\n\nStep 1: MCESU Endorsement\nStep 2: Receiving Facility Confirmation\nStep 3: Transport by Rescue 161\nStep 4: Full BFP SRU Decontamination"
        });

        // Page 8: DRRMO Signatories & Official Directory
        ProtocolPages.Add(new ProtocolPageItem
        {
            PageNumber = 8,
            SectionLetter = "H",
            Title = "DRRMO SIGNATORIES & DIRECTORY",
            Subtitle = "Official DRRMO Marikina City Contacts & Certification",
            ImageSource = "marikina_protocol_p6.jpg",
            HeaderBgColor = "#334155",
            TagColor = "#475569",
            IconData = "M19,3H5C3.9,3 3,3.9 3,5V19C3,20.1 3.9,21 5,21H19C20.1,21 21,20.1 21,19V5C21,3.9 20.1,3 19,3M14,17H7V15H14V17M17,13H7V11H17V13M17,9H7V7H17V9Z",
            Highlights = new ObservableCollection<string>
            {
                "DRRMO Headquarters: DRRMO BLDG., Fortune Ave., Brgy. Fortune, Marikina City.",
                "Telephone: 7116 5532 | Email: drrmo.marikinacity@gmail.com",
                "PREPARED BY: GYSSEL E. WALAWALA (Research and Planning Assistant)",
                "CHECKED BY: CITAS VIDA MAY D. CALUEN (Supervisor, Research & Planning Division)",
                "NOTED BY: DAVE C. DAVID, MBA (Head, Rescue 161 - DRRMO Marikina City)"
            },
            DetailedContent = "OFFICIAL DRRMO MARIKINA CITY PROTOCOL CERTIFICATION\n\nDRRMO BLDG. Fortune Ave., Brgy. Fortune, Marikina City, Metro Manila\nTel: 7116 5532 | Email: drrmo.marikinacity@gmail.com\n\nPrepared by: Gyssel E. Walawala\nChecked by: Citas Vida May D. Caluen\nNoted by: Dave C. David, MBA (Head, Rescue 161 DRRMO Marikina)"
        });
    }

    [RelayCommand]
    private void SelectPage(ProtocolPageItem? item)
    {
        if (item != null)
        {
            CurrentPage = item;
            SelectedPageIndex = ProtocolPages.IndexOf(item);
        }
    }

    [RelayCommand]
    private async Task ViewPdfAsync()
    {
        IsPdfModalVisible = true;

        if (PdfPageImages.Count > 0)
        {
            // Already loaded in memory - modal opens instantly (0 seconds delay)
            IsLoadingPdfPages = false;
            return;
        }

        IsLoadingPdfPages = true;

        const string fileName = "MarikinaDRRMO_PreparednessProtocol.pdf";
        var cachePath = Path.Combine(FileSystem.CacheDirectory, fileName);

        if (!File.Exists(cachePath))
        {
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
                using var dest = File.Create(cachePath);
                await stream.CopyToAsync(dest);
            }
            catch
            {
                // Fallback: Generate emergency protocol summary file if asset PDF is not present
                var textPath = Path.Combine(FileSystem.CacheDirectory, "Marikina_Disaster_Protocol_Summary.txt");
                await File.WriteAllTextAsync(textPath, BuildProtocolTextSummary(), Encoding.UTF8);
                cachePath = textPath;
            }
        }

        var pages = await PdfPageRenderService.RenderPdfPagesAsync(cachePath);
        PdfPageImages.Clear();
        foreach (var pageImg in pages)
        {
            PdfPageImages.Add(pageImg);
        }

        IsLoadingPdfPages = false;
    }

    [RelayCommand]
    private void ClosePdfModal()
    {
        IsPdfModalVisible = false;
    }

    [RelayCommand]
    private async Task DownloadPdfDocumentAsync()
    {
        IsDownloading = true;
        try
        {
            const string fileName = "MarikinaDRRMO_PreparednessProtocol.pdf";
            var cachePath = Path.Combine(FileSystem.CacheDirectory, fileName);

            if (!File.Exists(cachePath))
            {
                try
                {
                    using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
                    using var dest = File.Create(cachePath);
                    await stream.CopyToAsync(dest);
                }
                catch
                {
                    var textPath = Path.Combine(FileSystem.CacheDirectory, "Marikina_Disaster_Protocol_Summary.txt");
                    await File.WriteAllTextAsync(textPath, BuildProtocolTextSummary(), Encoding.UTF8);
                    cachePath = textPath;
                }
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Marikina Disaster Protocol Document",
                File = new ShareFile(cachePath)
            });
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Download Error", ex.Message, "OK");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private string BuildProtocolTextSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine("OFFICIAL MARIKINA CITY DISASTER PROTOCOL");
        sb.AppendLine("Rescue 161 - DRRMO Marikina Standard Operating Guidelines");
        sb.AppendLine("==================================================\n");

        foreach (var page in ProtocolPages)
        {
            sb.AppendLine($"[SECTION {page.SectionLetter}: {page.Title}]");
            sb.AppendLine($"{page.Subtitle}\n");
            sb.AppendLine(page.DetailedContent);
            sb.AppendLine("\n--------------------------------------------------\n");
        }

        sb.AppendLine("Emergency Hotlines: Dial 161 | 7116-5532 | 0928-5593341");
        return sb.ToString();
    }
}
