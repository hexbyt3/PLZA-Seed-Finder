using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PKHeX.Core;
using PLZASeedFinderPlugin.Helpers;

namespace PLZASeedFinderPlugin.GUI;

/// <summary>
/// PLZA Seed Finder Plugin for PKHeX
/// Provides comprehensive seed finding capabilities for Pokémon Legends: Z-A encounters.
/// Author: hexbyt3
/// </summary>
public partial class Gen9aSeedFinderForm : Form
{
    private readonly ISaveFileProvider _saveFileEditor;
    private readonly IPKMView _pkmEditor;
    private CancellationTokenSource? _searchCts;
    private List<SeedResult> _results = [];
    private List<EncounterWrapper> _cachedEncounters = [];
    private EncounterSource _availableSources;
    private List<ComboItem> _allSpecies = [];
    private readonly Lock _resultsLock = new();

    // Preview panel components
    private Panel _previewPanel = null!;
    private PictureBox _previewSprite = null!;
    private Label _previewTitle = null!;
    private Label _previewDetails = null!;
    private Label _previewStats = null!;
    private Label _previewMoves = null!;
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// Flags for different encounter sources in Generation 9a (PLZA).
    /// </summary>
    [Flags]
    private enum EncounterSource
    {
        None = 0,
        Wild = 1 << 0,
        Static = 1 << 1,
        Gift = 1 << 2,
        Trade = 1 << 3,
    }

    /// <summary>
    /// Initializes a new instance of the Gen9aSeedFinderForm.
    /// </summary>
    /// <param name="saveFileEditor">Save file provider interface</param>
    /// <param name="pkmEditor">PKM editor interface</param>
    public Gen9aSeedFinderForm(ISaveFileProvider saveFileEditor, IPKMView pkmEditor)
    {
        _saveFileEditor = saveFileEditor;
        _pkmEditor = pkmEditor;
        InitializeComponent();
        InitializePreviewPanel();
        LoadSpeciesList();
        LoadTrainerData();
        InitializeScaleCombo();
        SetupEventHandlers();

        // Cancel search when form is closing
        FormClosing += OnFormClosing;

        // Refresh trainer data when form is shown (after PKHeX has loaded save)
        Shown += OnFormShown;
    }

    /// <summary>
    /// Handles form shown event to refresh trainer data from save file.
    /// </summary>
    private void OnFormShown(object? sender, EventArgs e)
    {
        RefreshTrainerData();
    }

    /// <summary>
    /// Handles form closing to cancel any running search
    /// </summary>
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Cancel any running search operation
        _searchCts?.Cancel();
    }

    /// <summary>
    /// Initializes the preview panel components.
    /// </summary>
    private void InitializePreviewPanel()
    {
        _previewPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 200,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Control,
            Visible = false
        };

        _previewSprite = new PictureBox
        {
            Location = new Point(10, 10),
            Size = new Size(68, 56),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            BorderStyle = BorderStyle.FixedSingle
        };

        _previewTitle = new Label
        {
            Location = new Point(85, 10),
            Size = new Size(250, 20),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold)
        };

        _previewDetails = new Label
        {
            Location = new Point(85, 35),
            Size = new Size(250, 60),
            AutoSize = false
        };

        _previewStats = new Label
        {
            Location = new Point(340, 10),
            Size = new Size(180, 180),
            AutoSize = false,
            Font = new Font("Consolas", 9)
        };

        _previewMoves = new Label
        {
            Location = new Point(85, 100),
            Size = new Size(250, 90),
            AutoSize = false
        };

        _previewPanel.Controls.AddRange(new Control[] {
            _previewSprite, _previewTitle, _previewDetails, _previewStats, _previewMoves
        });

        // Add preview panel to results panel
        resultsPanel?.Controls.Add(_previewPanel);

        // Adjust grid size when preview is shown
        if (resultsGrid != null)
        {
            resultsGrid.SelectionChanged += ResultsGrid_SelectionChanged;
        }
    }

    /// <summary>
    /// Handles selection change events in the results grid to update preview.
    /// </summary>
    private void ResultsGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (resultsGrid.SelectedRows.Count == 0 || _previewPanel == null)
        {
            if (_previewPanel != null)
                _previewPanel.Visible = false;
            return;
        }

        if (resultsGrid.SelectedRows[0].Tag is SeedResult result)
        {
            UpdatePreviewPanel(result);

            // Adjust grid height to accommodate preview
            if (!_previewPanel.Visible)
            {
                _previewPanel.Visible = true;
                resultsGrid.Height = resultsPanel.Height - _previewPanel.Height;
            }
        }
    }

    /// <summary>
    /// Updates the preview panel with the selected result's information.
    /// </summary>
    /// <param name="result">Selected seed result</param>
    private void UpdatePreviewPanel(SeedResult result)
    {
        if (_previewSprite == null || _previewTitle == null || _previewDetails == null ||
            _previewStats == null || _previewMoves == null)
        {
            return;
        }

        var pk = result.Pokemon;
        var wrapper = new EncounterWrapper(result.Encounter, GameVersion.ZA);

        // Load sprite asynchronously
        _ = LoadPokemonSpriteAsync(pk);

        // Set title
        var speciesName = GameInfo.Strings.specieslist[pk.Species];
        var formName = pk.Form > 0 ? $" ({FormConverter.GetFormList(pk.Species, GameInfo.Strings.types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen9a)[pk.Form]})" : "";
        var shinyIndicator = pk.IsShiny ? (pk.ShinyXor == 0 ? " ■" : " ★") : "";
        _previewTitle.Text = $"{speciesName}{formName}{shinyIndicator}";
        _previewTitle.ForeColor = pk.IsShiny ? (pk.ShinyXor == 0 ? Color.DeepSkyBlue : Color.Gold) : SystemColors.ControlText;

        // Set details
        var details = new List<string>
        {
            $"Seed: {result.Seed:X16}",
            $"Nature: {pk.Nature} | Gender: {GetGenderSymbol(pk.Gender)}",
            $"Ability: {GetAbilityName(pk)} ({GetAbilityType(pk)})"
        };

        details.Add($"Encounter: {wrapper.GetDescription()}");

        _previewDetails.Text = string.Join("\n", details.Where(s => !string.IsNullOrEmpty(s)));

        // Set stats
        var stats = new[]
        {
            "Stats:",
            $"HP:  {pk.IV_HP,2} IV | {pk.Stat_HPMax,3} Total",
            $"Atk: {pk.IV_ATK,2} IV | {pk.Stat_ATK,3} Total",
            $"Def: {pk.IV_DEF,2} IV | {pk.Stat_DEF,3} Total",
            $"SpA: {pk.IV_SPA,2} IV | {pk.Stat_SPA,3} Total",
            $"SpD: {pk.IV_SPD,2} IV | {pk.Stat_SPD,3} Total",
            $"Spe: {pk.IV_SPE,2} IV | {pk.Stat_SPE,3} Total",
            "",
            $"Scale: {pk.Scale} | Height: {pk.HeightScalar} | Weight: {pk.WeightScalar}"
        };
        _previewStats.Text = string.Join("\n", stats);

        // Set moves
        var moveNames = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            var move = pk.GetMove(i);
            if (move != 0)
            {
                var moveName = GameInfo.Strings.movelist[move];
                moveNames.Add($"• {moveName}");
            }
        }
        _previewMoves.Text = moveNames.Count > 0 ? "Moves:\n" + string.Join("\n", moveNames) : "";
    }

    /// <summary>
    /// Gets the gender symbol for display.
    /// </summary>
    /// <param name="gender">Gender value</param>
    /// <returns>Gender symbol string</returns>
    private static string GetGenderSymbol(int gender) => gender switch
    {
        0 => "♂",
        1 => "♀",
        _ => "-"
    };

    /// <summary>
    /// Gets the ability type description.
    /// </summary>
    /// <param name="pk">Pokémon to check</param>
    /// <returns>Ability type string</returns>
    private static string GetAbilityType(PA9 pk) => pk.AbilityNumber switch
    {
        1 => "Ability 1",
        2 => "Ability 2",
        4 => "Hidden",
        _ => "?"
    };

    /// <summary>
    /// Loads the Pokémon sprite asynchronously from the web.
    /// </summary>
    /// <param name="pk">Pokémon to load sprite for</param>
    private async Task LoadPokemonSpriteAsync(PA9 pk)
    {
        try
        {
            var url = GetPokemonImageUrl(pk);

            // System.Diagnostics.Debug.WriteLine($"Loading sprite: {GameInfo.Strings.specieslist[pk.Species]} (G-Max: {pk.CanGigantamax}) from {url}");

            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                var image = Image.FromStream(stream);

                // Update UI on main thread
                if (_previewSprite.InvokeRequired)
                {
                    _previewSprite.Invoke(() => _previewSprite.Image = image);
                }
                else
                {
                    _previewSprite.Image = image;
                }
            }
            else
            {
                // System.Diagnostics.Debug.WriteLine($"Failed to load sprite: HTTP {response.StatusCode} for {url}");
                SetEmptySprite();
            }
        }
        catch (Exception)
        {
            // System.Diagnostics.Debug.WriteLine($"Error loading sprite: {ex.Message}");
            SetEmptySprite();
        }
    }

    /// <summary>
    /// Sets an empty sprite in the preview panel.
    /// </summary>
    private void SetEmptySprite()
    {
        if (_previewSprite.InvokeRequired)
        {
            _previewSprite.Invoke(() => _previewSprite.Image = null);
        }
        else
        {
            _previewSprite.Image = null;
        }
    }

    /// <summary>
    /// Gets the Pokémon image URL for HOME sprites.
    /// </summary>
    /// <param name="pk">Pokémon to get image for</param>
    /// <returns>Image URL string</returns>
    private static string GetPokemonImageUrl(PA9 pk)
    {
        var baseLink = "https://raw.githubusercontent.com/hexbyt3/HomeImages/master/128x128/poke_capture_0001_000_mf_n_00000000_f_n.png".Split('_');

        // PA9 does not support Gigantamax
        var canGmax = false;

        // Determine if we need gender-specific sprites
        bool md = false;
        bool fd = false;

        // Check for gender-dependent species (but NOT if G-Max)
        var genderDependentSpecies = new[]
        {
            (int)Species.Venusaur, (int)Species.Butterfree, (int)Species.Rattata, (int)Species.Raticate,
            (int)Species.Pikachu, (int)Species.Zubat, (int)Species.Golbat, (int)Species.Gloom,
            (int)Species.Vileplume, (int)Species.Kadabra, (int)Species.Alakazam, (int)Species.Doduo,
            (int)Species.Dodrio, (int)Species.Hypno, (int)Species.Goldeen, (int)Species.Seaking,
            (int)Species.Scyther, (int)Species.Magikarp, (int)Species.Gyarados, (int)Species.Eevee,
            (int)Species.Meganium, (int)Species.Ledyba, (int)Species.Ledian, (int)Species.Xatu,
            (int)Species.Sudowoodo, (int)Species.Politoed, (int)Species.Aipom, (int)Species.Wooper,
            (int)Species.Quagsire, (int)Species.Murkrow, (int)Species.Wobbuffet, (int)Species.Girafarig,
            (int)Species.Gligar, (int)Species.Steelix, (int)Species.Scizor, (int)Species.Heracross,
            (int)Species.Sneasel, (int)Species.Ursaring, (int)Species.Piloswine, (int)Species.Octillery,
            (int)Species.Houndoom, (int)Species.Donphan, (int)Species.Torchic, (int)Species.Combusken,
            (int)Species.Blaziken, (int)Species.Beautifly, (int)Species.Dustox, (int)Species.Ludicolo,
            (int)Species.Nuzleaf, (int)Species.Shiftry, (int)Species.Swalot, (int)Species.Camerupt,
            (int)Species.Cacturne, (int)Species.Milotic, (int)Species.Relicanth, (int)Species.Starly,
            (int)Species.Staravia, (int)Species.Staraptor, (int)Species.Bidoof, (int)Species.Bibarel,
            (int)Species.Kricketot, (int)Species.Kricketune, (int)Species.Shinx, (int)Species.Luxio,
            (int)Species.Luxray, (int)Species.Roserade, (int)Species.Combee, (int)Species.Pachirisu,
            (int)Species.Buizel, (int)Species.Floatzel, (int)Species.Ambipom, (int)Species.Gible,
            (int)Species.Gabite, (int)Species.Garchomp, (int)Species.Hippopotas, (int)Species.Hippowdon,
            (int)Species.Croagunk, (int)Species.Toxicroak, (int)Species.Finneon, (int)Species.Lumineon,
            (int)Species.Snover, (int)Species.Abomasnow, (int)Species.Weavile, (int)Species.Rhyperior,
            (int)Species.Tangrowth, (int)Species.Mamoswine, (int)Species.Unfezant, (int)Species.Frillish,
            (int)Species.Jellicent, (int)Species.Pyroar, (int)Species.Meowstic, (int)Species.Indeedee
        };

        if (genderDependentSpecies.Contains(pk.Species) && !canGmax && pk.Form == 0)
        {
            if (pk.Gender == 0 && pk.Species != (int)Species.Torchic)
                md = true;
            else
                fd = true;
        }

        // Special case for Sneasel
        if (pk.Species == (int)Species.Sneasel)
        {
            if (pk.Gender == 0)
                md = true;
            else
                fd = true;
        }

        // Species number formatting
        baseLink[2] = pk.Species < 10 ? $"000{pk.Species}" :
                      pk.Species < 100 ? $"00{pk.Species}" :
                      pk.Species < 1000 ? $"0{pk.Species}" :
                      $"{pk.Species}";

        // Form number formatting with special cases
        int form = pk.Species switch
        {
            (int)Species.Sinistea or (int)Species.Polteageist or (int)Species.Rockruff or (int)Species.Mothim => 0,
            (int)Species.Alcremie when pk.IsShiny || canGmax => 0,
            _ => pk.Form,
        };
        baseLink[3] = form < 10 ? $"00{form}" : $"0{form}";

        // Gender designation
        baseLink[4] = pk.PersonalInfo.OnlyFemale ? "fo" :
                      pk.PersonalInfo.OnlyMale ? "mo" :
                      pk.PersonalInfo.Genderless ? "uk" :
                      fd ? "fd" :
                      md ? "md" :
                      "mf";

        // Gigantamax
        baseLink[5] = canGmax ? "g" : "n";

        // Form argument (for Alcremie, etc.)
        baseLink[6] = pk.Species == (int)Species.Alcremie && !canGmax ?
                      $"0000000{pk.FormArgument}" :
                      "00000000";

        // Shiny status
        baseLink[8] = pk.IsShiny ? "r.png" : "n.png";

        return string.Join("_", baseLink);
    }

    /// <summary>
    /// Sets up event handlers and enables double buffering for the results grid.
    /// </summary>
    private void SetupEventHandlers()
    {
        // Enable double buffering for smoother updates
        typeof(DataGridView).InvokeMember("DoubleBuffered",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
            null, resultsGrid, [true]);

        // Hook up encounter combo to disable scale for Alpha encounters
        encounterCombo.SelectedIndexChanged += EncounterCombo_SelectedIndexChanged;
    }

    /// <summary>
    /// Handles encounter combo selection changes to disable scale for Alpha encounters.
    /// </summary>
    private void EncounterCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // Check if selected encounter is an Alpha
        var selectedIndex = encounterCombo.SelectedValue as int? ?? -1;
        if (selectedIndex < 0)
        {
            // "All Encounters" is selected, enable scale combo
            scaleCombo.Enabled = true;
            return;
        }

        // Get the selected encounter description
        var selectedEncounterText = (encounterCombo.SelectedItem as ComboItem)?.Text;
        if (string.IsNullOrEmpty(selectedEncounterText))
        {
            scaleCombo.Enabled = true;
            return;
        }

        // Get the current form
        var form = (byte)(formCombo.SelectedValue as int? ?? 0);

        // Find all encounters matching this description and form
        var matchingEncounters = _cachedEncounters
            .Where(e => e.GetDescription() == selectedEncounterText && (e.Form == form || e.Form >= EncounterUtil.FormDynamic))
            .ToList();

        // Check if any of the matching encounters are Alpha
        var hasAlpha = matchingEncounters.Any(e => e.Encounter is EncounterSlot9a { IsAlpha: true }
                                                 || e.Encounter is EncounterStatic9a { IsAlpha: true }
                                                 || e.Encounter is EncounterGift9a { IsAlpha: true });

        // Disable scale selection for Alpha encounters (they're always 255)
        scaleCombo.Enabled = !hasAlpha;
        if (hasAlpha)
        {
            scaleCombo.SelectedIndex = 0; // Set to "Any" when disabled
        }
    }

    /// <summary>
    /// Loads trainer data from the current save file.
    /// </summary>
    private void LoadTrainerData()
    {
        var sav = _saveFileEditor.SAV;

        // Check if we have a valid save file loaded
        if (sav == null)
            return;

        // PLZA uses the Gen 7+ display format (6-digit TID, 4-digit SID)
        // Convert from internal 16-bit values to display format
        uint id32 = ((uint)sav.SID16 << 16) | sav.TID16;

        // Only update if we have non-zero IDs (indicating a real save file)
        if (id32 != 0)
        {
            tidNum.Value = id32 % 1000000; // TID7 (0-999999)
            sidNum.Value = id32 / 1000000; // SID7 (0-4294)
        }

        // Load trainer name
        if (!string.IsNullOrWhiteSpace(sav.OT))
        {
            trainerNameText.Text = sav.OT;
        }
    }

    /// <summary>
    /// Refreshes trainer data from the current save file.
    /// Called when the form is shown or save file changes.
    /// </summary>
    private void RefreshTrainerData()
    {
        LoadTrainerData();
    }

    /// <summary>
    /// Loads the species list for PLZA Pokémon with encounters.
    /// </summary>
    private void LoadSpeciesList()
    {
        // Gather all species that have encounters in PLZA
        var validSpecies = new HashSet<ushort>();

        // Add wild encounter species
        foreach (var area in Encounters9a.Slots)
        {
            foreach (var slot in area.Slots)
                validSpecies.Add(slot.Species);
        }

        // Add static encounter species
        foreach (var encounter in Encounters9a.Static)
            validSpecies.Add(encounter.Species);

        // Add gift encounter species
        foreach (var encounter in Encounters9a.Gifts)
            validSpecies.Add(encounter.Species);

        // Add trade encounter species
        foreach (var encounter in Encounters9a.Trades)
            validSpecies.Add(encounter.Species);

        // Build combo items from valid species
        var species = new List<ComboItem>();
        var names = GameInfo.Strings.specieslist;

        foreach (var speciesId in validSpecies.OrderBy(x => x))
        {
            if (speciesId < names.Length)
                species.Add(new ComboItem(names[speciesId], speciesId));
        }

        _allSpecies = species;
        speciesCombo.DisplayMember = "Text";
        speciesCombo.ValueMember = "Value";
        speciesCombo.DataSource = species;
    }

    /// <summary>
    /// Handles the species search box text changed event to filter species list.
    /// </summary>
    private void SpeciesSearchBox_TextChanged(object? sender, EventArgs e)
    {
        var searchText = speciesSearchBox.Text.Trim();

        if (string.IsNullOrEmpty(searchText))
        {
            speciesCombo.DataSource = _allSpecies;
            return;
        }

        var filteredSpecies = _allSpecies.Where(s =>
            s.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filteredSpecies.Count > 0)
        {
            speciesCombo.DataSource = filteredSpecies;
            if (filteredSpecies.Count == 1)
                speciesCombo.SelectedIndex = 0;
        }
        else
        {
            speciesCombo.DataSource = new List<ComboItem>();
        }
    }

    /// <summary>
    /// Handles species selection change event.
    /// </summary>
    private void SpeciesCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (speciesCombo.SelectedValue is not int species)
            return;

        UpdateFormList(species);
        UpdateEncounterList(species);
        UpdateSourceDisplay();
        UpdateEncounterCombo();
    }

    /// <summary>
    /// Updates the form list for the selected species.
    /// </summary>
    /// <param name="species">Species ID</param>
    private void UpdateFormList(int species)
    {
        var forms = FormConverter.GetFormList((ushort)species, GameInfo.Strings.types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen9a);

        formCombo.DisplayMember = "Text";
        formCombo.ValueMember = "Value";
        formCombo.DataSource = forms.Select((f, i) => new ComboItem(f, i)).ToList();
    }

    /// <summary>
    /// Updates the encounter list for the selected species based on selected sources.
    /// </summary>
    /// <param name="species">Species ID</param>
    private void UpdateEncounterList(int species)
    {
        var encounters = new List<EncounterWrapper>();
        _availableSources = EncounterSource.None;

        var selectedSources = GetSelectedSources();

        // Get wild encounters
        if (selectedSources.HasFlag(EncounterSource.Wild))
        {
            var wildEncounters = GetWildEncounters((ushort)species, GameVersion.ZA);
            encounters.AddRange(wildEncounters.Select(e => new EncounterWrapper(e, GameVersion.ZA)));
            if (wildEncounters.Count > 0)
                _availableSources |= EncounterSource.Wild;
        }

        // Get static encounters
        if (selectedSources.HasFlag(EncounterSource.Static))
        {
            var staticEncounters = GetStaticEncounters((ushort)species);
            encounters.AddRange(staticEncounters.Select(e => new EncounterWrapper(e, GameVersion.ZA)));
            if (staticEncounters.Count > 0)
                _availableSources |= EncounterSource.Static;
        }

        // Get gift encounters
        if (selectedSources.HasFlag(EncounterSource.Gift))
        {
            var giftEncounters = GetGiftEncounters((ushort)species, GameVersion.ZA);
            encounters.AddRange(giftEncounters.Select(e => new EncounterWrapper(e, GameVersion.ZA)));
            if (giftEncounters.Count > 0)
                _availableSources |= EncounterSource.Gift;
        }

        // Get trade encounters
        if (selectedSources.HasFlag(EncounterSource.Trade))
        {
            var tradeEncounters = GetTradeEncounters((ushort)species);
            encounters.AddRange(tradeEncounters.Select(e => new EncounterWrapper(e, GameVersion.ZA)));
            if (tradeEncounters.Count > 0)
                _availableSources |= EncounterSource.Trade;
        }

        _cachedEncounters = encounters;
    }

    /// <summary>
    /// Gets the currently selected encounter sources from checkboxes.
    /// </summary>
    /// <returns>Combined encounter source flags</returns>
    private EncounterSource GetSelectedSources()
    {
        var sources = EncounterSource.None;
        if (wildEncounterCheck.Checked) sources |= EncounterSource.Wild;
        if (staticEncounterCheck.Checked) sources |= EncounterSource.Static;
        if (giftEncounterCheck.Checked) sources |= EncounterSource.Gift;
        if (tradeEncounterCheck.Checked) sources |= EncounterSource.Trade;
        return sources == EncounterSource.None ? EncounterSource.Wild | EncounterSource.Static | EncounterSource.Gift | EncounterSource.Trade : sources;
    }

    /// <summary>
    /// Updates the encounter combo box with available encounters.
    /// </summary>
    private void UpdateEncounterCombo()
    {
        var items = new List<ComboItem> { new("All Encounters", -1) };

        var form = (byte)(formCombo.SelectedValue as int? ?? 0);
        var groupedEncounters = _cachedEncounters
            .Where(e => e.Form == form || e.Form >= EncounterUtil.FormDynamic)
            .GroupBy(e => e.GetDescription())
            .Select((g, i) => new { Description = g.Key, Index = i })
            .ToList();

        items.AddRange(groupedEncounters.Select(g => new ComboItem(g.Description, g.Index)));

        encounterCombo.DisplayMember = "Text";
        encounterCombo.ValueMember = "Value";
        encounterCombo.DataSource = items;
        encounterCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Updates the status display with available encounter sources.
    /// </summary>
    private void UpdateSourceDisplay()
    {
        var sources = new List<string>();
        if (_availableSources.HasFlag(EncounterSource.Wild))
            sources.Add("Wild");
        if (_availableSources.HasFlag(EncounterSource.Static))
            sources.Add("Static");
        if (_availableSources.HasFlag(EncounterSource.Gift))
            sources.Add("Gift");
        if (_availableSources.HasFlag(EncounterSource.Trade))
            sources.Add("Trade");

        statusLabel.Text = sources.Count > 0
            ? $"Available in: {string.Join(", ", sources)}"
            : "No encounters found";
    }

    /// <summary>
    /// Gets wild encounters for a specific species.
    /// </summary>
    /// <param name="species">Species ID</param>
    /// <param name="version">Game version</param>
    /// <returns>List of wild encounters</returns>
    private static List<EncounterSlot9a> GetWildEncounters(ushort species, GameVersion version)
    {
        var encounters = new List<EncounterSlot9a>();
        var areas = Encounters9a.Slots;

        foreach (var area in areas)
        {
            foreach (var slot in area.Slots)
            {
                if (slot.Species == species)
                    encounters.Add(slot);
            }
        }

        return encounters;
    }

    /// <summary>
    /// Gets static encounters for a specific species.
    /// </summary>
    /// <param name="species">Species ID</param>
    /// <returns>List of static encounters</returns>
    private static List<EncounterStatic9a> GetStaticEncounters(ushort species)
    {
        var encounters = new List<EncounterStatic9a>();

        foreach (var enc in Encounters9a.Static)
        {
            if (enc.Species == species)
                encounters.Add(enc);
        }

        return encounters;
    }

    /// <summary>
    /// Gets gift encounters for a specific species.
    /// </summary>
    /// <param name="species">Species ID</param>
    /// <param name="version">Game version</param>
    /// <returns>List of gift encounters</returns>
    private static List<EncounterGift9a> GetGiftEncounters(ushort species, GameVersion version)
    {
        var encounters = new List<EncounterGift9a>();

        foreach (var enc in Encounters9a.Gifts)
        {
            if (enc.Species == species)
                encounters.Add(enc);
        }

        return encounters;
    }

    /// <summary>
    /// Gets trade encounters for a specific species.
    /// </summary>
    /// <param name="species">Species ID</param>
    /// <returns>List of trade encounters</returns>
    private static List<EncounterTrade9a> GetTradeEncounters(ushort species)
    {
        var encounters = new List<EncounterTrade9a>();

        foreach (var enc in Encounters9a.Trades)
        {
            if (enc.Species == species)
                encounters.Add(enc);
        }

        return encounters;
    }

    /// <summary>
    /// Handles the search button click event to start or stop seed searching.
    /// </summary>
    private async void SearchButton_Click(object? sender, EventArgs e)
    {
        if (_searchCts != null)
        {
            _searchCts.Cancel();
            return;
        }

        if (speciesCombo.SelectedValue is not int species)
        {
            WinFormsUtil.Alert("Please select a species!");
            return;
        }

        var form = (byte)(formCombo.SelectedValue as int? ?? 0);
        var criteria = GetCriteria();

        // Capture UI values before background task
        var encounterIndex = encounterCombo.SelectedValue as int? ?? -1;
        var selectedEncounterText = (encounterCombo.SelectedItem as ComboItem)?.Text;
        var ivRanges = GetIVRanges();
        var scaleRange = GetScaleRange();
        var sizeType = GetSizeType9();
        var maxResults = (int)maxSeedsNum.Value;

        lock (_resultsLock)
        {
            _results.Clear();
        }
        resultsGrid.Rows.Clear();

        // Hide preview panel when starting new search
        if (_previewPanel != null && _previewPanel.Visible)
        {
            _previewPanel.Visible = false;
            resultsGrid.Height = resultsPanel.Height;
        }

        searchButton.Text = "Stop";
        progressBar.Visible = true;
        statusLabel.Text = "Searching...";

        _searchCts = new CancellationTokenSource();

        try
        {
            await Task.Run(() => SearchSeeds(species, form, criteria, encounterIndex, selectedEncounterText, ivRanges, scaleRange, sizeType, maxResults, _searchCts.Token));
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !statusLabel.IsDisposed)
                statusLabel.Text = "Search cancelled";
        }
        finally
        {
            if (!IsDisposed)
            {
                if (!searchButton.IsDisposed)
                    searchButton.Text = "Search";
                if (!progressBar.IsDisposed)
                    progressBar.Visible = false;
            }
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }

    /// <summary>
    /// Represents an IV range with minimum and maximum values.
    /// </summary>
    private record struct IVRange(int Min, int Max);

    /// <summary>
    /// Represents a Scale range with minimum and maximum values.
    /// </summary>
    private record struct ScaleRange(int Min, int Max);

    /// <summary>
    /// Scale size type options for the dropdown.
    /// </summary>
    private enum ScaleSizeType
    {
        Any,
        XS,
        S,
        M,
        L,
        XL
    }

    /// <summary>
    /// Gets the encounter criteria from the UI controls.
    /// </summary>
    /// <returns>Encounter criteria for searching</returns>
    private EncounterCriteria GetCriteria()
    {
        var genderIndex = genderCombo.SelectedIndex;
        var gender = genderIndex switch
        {
            0 => Gender.Random,
            1 => (Gender)0, // Male
            2 => (Gender)1, // Female
            3 => (Gender)2, // Genderless
            _ => Gender.Random
        };

        var criteria = new EncounterCriteria
        {
            Gender = gender,
            Ability = GetAbilityPermission(),
            Nature = natureCombo.SelectedIndex == 0 ? Nature.Random : (Nature)(natureCombo.SelectedIndex - 1),
            Shiny = GetShinyType(),
        };

        return criteria;
    }

    /// <summary>
    /// Gets the selected shiny type from the UI.
    /// </summary>
    /// <returns>Shiny type selection</returns>
    private Shiny GetShinyType()
    {
        return shinyCombo.SelectedIndex switch
        {
            0 => Shiny.Random,
            1 => Shiny.Never,
            2 => Shiny.Always,
            3 => Shiny.AlwaysSquare,
            4 => Shiny.AlwaysStar,
            _ => Shiny.Random
        };
    }

    /// <summary>
    /// Gets the IV ranges from the UI controls.
    /// </summary>
    /// <returns>Array of IV ranges for each stat</returns>
    private IVRange[] GetIVRanges()
    {
        return
        [
            new IVRange((int)ivHpMin.Value, (int)ivHpMax.Value),
            new IVRange((int)ivAtkMin.Value, (int)ivAtkMax.Value),
            new IVRange((int)ivDefMin.Value, (int)ivDefMax.Value),
            new IVRange((int)ivSpaMin.Value, (int)ivSpaMax.Value),
            new IVRange((int)ivSpdMin.Value, (int)ivSpdMax.Value),
            new IVRange((int)ivSpeMin.Value, (int)ivSpeMax.Value),
        ];
    }

    /// <summary>
    /// Initializes the scale dropdown with size presets.
    /// </summary>
    private void InitializeScaleCombo()
    {
        scaleCombo.Items.Clear();
        scaleCombo.Items.Add(new ComboItem("Any", (int)ScaleSizeType.Any));
        scaleCombo.Items.Add(new ComboItem("XS", (int)ScaleSizeType.XS));
        scaleCombo.Items.Add(new ComboItem("S", (int)ScaleSizeType.S));
        scaleCombo.Items.Add(new ComboItem("M", (int)ScaleSizeType.M));
        scaleCombo.Items.Add(new ComboItem("L", (int)ScaleSizeType.L));
        scaleCombo.Items.Add(new ComboItem("XL", (int)ScaleSizeType.XL));
        scaleCombo.DisplayMember = "Text";
        scaleCombo.ValueMember = "Value";
        scaleCombo.SelectedIndex = 0; // Default to "Any"
    }

    /// <summary>
    /// Gets the Scale range from the UI controls based on the selected size type.
    /// </summary>
    /// <returns>Scale range for searching</returns>
    private ScaleRange GetScaleRange()
    {
        var selectedType = (ScaleSizeType)(scaleCombo.SelectedValue as int? ?? 0);
        return selectedType switch
        {
            ScaleSizeType.XS => new ScaleRange(0, 15),      // < 0x10
            ScaleSizeType.S => new ScaleRange(16, 47),      // >= 0x10 and < 0x30
            ScaleSizeType.M => new ScaleRange(48, 207),     // >= 0x30 and < 0xD0
            ScaleSizeType.L => new ScaleRange(208, 239),    // >= 0xD0 and < 0xF0
            ScaleSizeType.XL => new ScaleRange(240, 255),   // >= 0xF0
            _ => new ScaleRange(0, 255),                    // Any/RANDOM
        };
    }

    /// <summary>
    /// Converts the UI scale size type to PKHeX SizeType9.
    /// </summary>
    /// <returns>SizeType9 for use in GenerateParam9a</returns>
    private SizeType9 GetSizeType9()
    {
        var selectedType = (ScaleSizeType)(scaleCombo.SelectedValue as int? ?? 0);
        return selectedType switch
        {
            ScaleSizeType.XS => SizeType9.XS,
            ScaleSizeType.S => SizeType9.S,
            ScaleSizeType.M => SizeType9.M,
            ScaleSizeType.L => SizeType9.L,
            ScaleSizeType.XL => SizeType9.XL,
            _ => SizeType9.RANDOM,
        };
    }

    /// <summary>
    /// Gets the selected ability permission from the UI.
    /// </summary>
    /// <returns>Ability permission selection</returns>
    private AbilityPermission GetAbilityPermission()
    {
        return abilityCombo.SelectedIndex switch
        {
            0 => AbilityPermission.Any12H,
            1 => AbilityPermission.OnlyFirst,
            2 => AbilityPermission.OnlySecond,
            3 => AbilityPermission.OnlyHidden,
            4 => AbilityPermission.Any12,
            _ => AbilityPermission.Any12H
        };
    }

    /// <summary>
    /// Gets trainer information from the save file.
    /// </summary>
    /// <returns>Trainer info for generation</returns>
    private ITrainerInfo GetTrainerInfo()
    {
        var version = _saveFileEditor.SAV.Version;
        if (version is not (GameVersion.ZA or GameVersion.ZA))
            version = GameVersion.ZA;

        // Convert from display format (TID7/SID7) to ID32, then extract 16-bit values
        uint id32 = ((uint)sidNum.Value * 1000000) + (uint)tidNum.Value;
        ushort tid16 = (ushort)(id32 & 0xFFFF);
        ushort sid16 = (ushort)(id32 >> 16);

        return new SimpleTrainerInfo(version)
        {
            TID16 = tid16,
            SID16 = sid16,
            OT = _saveFileEditor.SAV.OT,
            Gender = _saveFileEditor.SAV.Gender,
            Language = _saveFileEditor.SAV.Language,
        };
    }

    /// <summary>
    /// Searches for seeds that match the specified criteria.
    /// </summary>
    /// <param name="species">Target species ID</param>
    /// <param name="form">Target form</param>
    /// <param name="criteria">Search criteria</param>
    /// <param name="encounterIndex">Selected encounter index</param>
    /// <param name="selectedEncounterText">Selected encounter description</param>
    /// <param name="ivRanges">IV ranges to search for</param>
    /// <param name="scaleRange">Scale range to search for</param>
    /// <param name="sizeType">Size type for scale generation</param>
    /// <param name="maxResults">Maximum number of results</param>
    /// <param name="token">Cancellation token</param>
    private void SearchSeeds(int species, byte form, EncounterCriteria criteria, int encounterIndex, string? selectedEncounterText, IVRange[] ivRanges, ScaleRange scaleRange, SizeType9 sizeType, int maxResults, CancellationToken token)
    {
        var results = new List<SeedResult>();

        // Parse seed range
        if (!TryParseSeedRange(out var startSeed, out var endSeed))
            return;

        ulong totalSeeds = endSeed - startSeed + 1;
        ulong seedsChecked = 0;
        ulong lastProgressUpdate = 0;
        const ulong updateInterval = 50000; // Update less frequently

        var tr = GetTrainerInfo();
        var encountersToCheck = GetEncountersToCheck(form, encounterIndex, selectedEncounterText);

        // Use parallel processing for better performance
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        var seedBatch = new List<ulong>();
        const int batchSize = 10000;

        for (ulong seed = startSeed; seed <= endSeed && results.Count < maxResults && !token.IsCancellationRequested; seed++)
        {
            seedBatch.Add(seed);

            if (seedBatch.Count >= batchSize || seed == endSeed)
            {
                var batchResults = new List<SeedResult>();

                Parallel.ForEach(seedBatch, parallelOptions, currentSeed =>
                {
                    foreach (var wrapper in encountersToCheck)
                    {
                        // First, quickly verify if seed is valid without full generation
                        if (!QuickVerifySeed(wrapper.Encounter, currentSeed, criteria, ivRanges, scaleRange, sizeType, tr))
                            continue;

                        // Only generate full Pokemon for valid seeds
                        var pk = TryGeneratePokemon(wrapper.Encounter, currentSeed, criteria, sizeType, tr, form);
                        if (pk == null)
                            continue;

                        // Verify all criteria including shiny, nature, gender, ability, IVs, and scale
                        if (!CheckPokemonMatchesCriteria(pk, criteria, ivRanges, scaleRange))
                            continue;

                        var result = new SeedResult
                        {
                            Seed = currentSeed,
                            Encounter = wrapper.Encounter,
                            Pokemon = pk
                        };

                        lock (batchResults)
                        {
                            batchResults.Add(result);
                        }
                        break;
                    }
                });

                // Add batch results
                foreach (var result in batchResults.OrderBy(r => r.Seed))
                {
                    if (results.Count >= maxResults)
                        break;

                    results.Add(result);
                    AddResultToGrid(result);
                }

                seedsChecked += (ulong)seedBatch.Count;
                seedBatch.Clear();

                if (seedsChecked - lastProgressUpdate >= updateInterval)
                {
                    lastProgressUpdate = seedsChecked;
                    try
                    {
                        this.Invoke(() =>
                        {
                            if (!IsDisposed && !progressBar.IsDisposed && !statusLabel.IsDisposed)
                            {
                                var progressPercent = (int)((seedsChecked / (double)totalSeeds) * 100);
                                progressBar.Value = Math.Min(progressPercent, 100);
                                statusLabel.Text = $"Checked {seedsChecked:N0} ({progressPercent}%), found {results.Count}";
                            }
                        });
                    }
                    catch (ObjectDisposedException)
                    {
                        // Form was disposed while updating, ignore
                    }
                }
            }
        }

        lock (_resultsLock)
        {
            _results = results;
        }

        try
        {
            this.Invoke(() =>
            {
                if (!IsDisposed && !statusLabel.IsDisposed && !progressBar.IsDisposed)
                {
                    statusLabel.Text = $"Found {results.Count} matches after checking {seedsChecked:N0} seeds";
                    progressBar.Value = 100;
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // Form was disposed while updating, ignore
        }
    }

    /// <summary>
    /// Quickly verifies if a seed matches the search criteria without generating a full Pokémon.
    /// </summary>
    /// <param name="encounter">Encounter to verify against</param>
    /// <param name="seed">Seed value to check</param>
    /// <param name="criteria">Search criteria</param>
    /// <param name="ivRanges">IV ranges to validate</param>
    /// <param name="scaleRange">Scale range to validate</param>
    /// <param name="sizeType">Size type for scale generation</param>
    /// <param name="tr">Trainer information</param>
    /// <returns>True if the seed potentially matches criteria, false otherwise</returns>
    private bool QuickVerifySeed(object encounter, ulong seed, EncounterCriteria criteria, IVRange[] ivRanges, ScaleRange scaleRange, SizeType9 sizeType, ITrainerInfo tr)
    {
        // Get PersonalInfo to pass to GetParams
        var pi = encounter switch
        {
            EncounterSlot9a slot => PersonalTable.ZA[slot.Species, slot.Form],
            EncounterStatic9a static9a => PersonalTable.ZA[static9a.Species, static9a.Form],
            EncounterGift9a gift => PersonalTable.ZA[gift.Species, gift.Form],
            EncounterTrade9a trade => PersonalTable.ZA[trade.Species, trade.Form],
            _ => null
        };

        if (pi == null)
            return false;

        // Use each encounter's GetParams method to get the proper parameters with correlation
        var param = encounter switch
        {
            EncounterSlot9a slot => slot.GetParams((PersonalInfo9ZA)pi),
            EncounterStatic9a static9a => static9a.GetParams((PersonalInfo9ZA)pi),
            EncounterGift9a gift => gift.GetParams((PersonalInfo9ZA)pi),
            EncounterTrade9a trade => trade.GetParams((PersonalInfo9ZA)pi),
            _ => default
        };

        if (param.GenderRatio == 0 && encounter is not EncounterTrade9a)
            return false;

        // Only override SizeType for non-Alpha encounters
        // Alpha encounters must use SizeType.VALUE with Scale=255
        var isAlpha = encounter switch
        {
            EncounterSlot9a slot => slot.IsAlpha,
            EncounterStatic9a static9a => static9a.IsAlpha,
            EncounterGift9a gift => gift.IsAlpha,
            _ => false
        };

        if (!isAlpha)
        {
            param = param with { SizeType = sizeType };
        }

        // For PA9, we create a temporary Pokémon and use LumioseRNG to verify
        // This is more reliable than trying to replicate complex PA9 RNG logic
        var pk = new PA9 { Species = encounter switch {
            EncounterSlot9a s => s.Species,
            EncounterStatic9a s => s.Species,
            EncounterGift9a g => g.Species,
            EncounterTrade9a t => t.Species,
            _ => 0
        }};

        if (pk.Species == 0)
            return false;

        // Use LumioseRNG to generate the Pokemon with this seed
        if (!LumioseRNG.GenerateData(pk, param, criteria, seed))
            return false;

        // Check IV ranges
        Span<int> ivs = stackalloc int[6];
        ivs[0] = pk.IV_HP;
        ivs[1] = pk.IV_ATK;
        ivs[2] = pk.IV_DEF;
        ivs[3] = pk.IV_SPA;
        ivs[4] = pk.IV_SPD;
        ivs[5] = pk.IV_SPE;

        if (!CheckIVRangesSpan(ivs, ivRanges))
            return false;

        // Additional criteria checks
        if (criteria.Gender != Gender.Random && pk.Gender != (byte)criteria.Gender)
            return false;

        if (criteria.Nature != Nature.Random && pk.Nature != criteria.Nature)
            return false;

        if (criteria.Ability != AbilityPermission.Any12H)
        {
            if (!CheckAbilityQuick(pk.AbilityNumber, criteria.Ability))
                return false;
        }

        // Check shiny criteria
        bool matchesShiny = criteria.Shiny switch
        {
            Shiny.Never => !pk.IsShiny,
            Shiny.Always => pk.IsShiny,
            Shiny.AlwaysSquare => pk.IsShiny && pk.ShinyXor == 0,
            Shiny.AlwaysStar => pk.IsShiny && pk.ShinyXor > 0 && pk.ShinyXor < 16,
            _ => true // Shiny.Random accepts any
        };

        if (!matchesShiny)
            return false;

        // Check scale
        if (pk.Scale < scaleRange.Min || pk.Scale > scaleRange.Max)
            return false;

        return true;
    }

    /// <summary>
    /// Checks if the given IVs are within the specified ranges using a Span for performance.
    /// </summary>
    /// <param name="ivs">Span containing the 6 IV values</param>
    /// <param name="ranges">Array of IV ranges to validate against</param>
    /// <returns>True if all IVs are within their respective ranges, false otherwise</returns>
    private static bool CheckIVRangesSpan(Span<int> ivs, IVRange[] ranges)
    {
        return ivs[0] >= ranges[0].Min && ivs[0] <= ranges[0].Max &&
               ivs[1] >= ranges[1].Min && ivs[1] <= ranges[1].Max &&
               ivs[2] >= ranges[2].Min && ivs[2] <= ranges[2].Max &&
               ivs[3] >= ranges[3].Min && ivs[3] <= ranges[3].Max &&
               ivs[4] >= ranges[4].Min && ivs[4] <= ranges[4].Max &&
               ivs[5] >= ranges[5].Min && ivs[5] <= ranges[5].Max;
    }

    /// <summary>
    /// Quickly checks if an ability number matches the specified criteria.
    /// </summary>
    /// <param name="abilityNumber">The ability slot number (0-2)</param>
    /// <param name="criteria">The ability permission criteria</param>
    /// <returns>True if the ability matches criteria, false otherwise</returns>
    private static bool CheckAbilityQuick(int abilityNumber, AbilityPermission criteria)
    {
        return (criteria, abilityNumber) switch
        {
            (AbilityPermission.OnlyFirst, 0) => true,
            (AbilityPermission.OnlySecond, 1) => true,
            (AbilityPermission.OnlyHidden, 2) => true,
            (AbilityPermission.Any12, 0 or 1) => true,
            _ => false
        };
    }

    /// <summary>
    /// Tries to parse the seed range from the UI text boxes.
    /// </summary>
    /// <param name="startSeed">Parsed start seed value</param>
    /// <param name="endSeed">Parsed end seed value</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    private bool TryParseSeedRange(out ulong startSeed, out ulong endSeed)
    {
        startSeed = 0x0000000000000000;
        endSeed = 0xFFFFFFFFFFFFFFFF;

        if (!string.IsNullOrEmpty(startSeedTextBox?.Text))
        {
            if (!ulong.TryParse(startSeedTextBox.Text, System.Globalization.NumberStyles.HexNumber, null, out startSeed))
            {
                this.Invoke(() => WinFormsUtil.Alert("Invalid start seed format. Using default 0000000000000000."));
                return false;
            }
        }

        if (!string.IsNullOrEmpty(endSeedTextBox?.Text))
        {
            if (!ulong.TryParse(endSeedTextBox.Text, System.Globalization.NumberStyles.HexNumber, null, out endSeed))
            {
                this.Invoke(() => WinFormsUtil.Alert("Invalid end seed format. Using default FFFFFFFFFFFFFFFF."));
                return false;
            }
        }

        if (startSeed > endSeed)
        {
            this.Invoke(() => WinFormsUtil.Alert("Start seed must be less than or equal to end seed!"));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the list of encounters to check based on form and selection.
    /// </summary>
    /// <param name="form">Target form</param>
    /// <param name="encounterIndex">Selected encounter index</param>
    /// <param name="selectedEncounterText">Selected encounter description</param>
    /// <returns>List of encounter wrappers to check</returns>
    private List<EncounterWrapper> GetEncountersToCheck(byte form, int encounterIndex, string? selectedEncounterText)
    {
        if (encounterIndex == -1)
        {
            return _cachedEncounters.Where(e => e.Form == form || e.Form >= EncounterUtil.FormDynamic).ToList();
        }

        return _cachedEncounters.Where(e => e.GetDescription() == selectedEncounterText && (e.Form == form || e.Form >= EncounterUtil.FormDynamic)).ToList();
    }

    /// <summary>
    /// Creates a PA9 from an encounter with a specific seed.
    /// </summary>
    private PA9? CreatePA9FromEncounter(IEncounter9a encounter, ITrainerInfo tr, GenerateParam9a param, EncounterCriteria criteria, ulong seed, PersonalInfo9ZA pi)
    {
        int lang = (int)Language.GetSafeLanguage9a((LanguageID)tr.Language);
        var pk = new PA9
        {
            Language = lang,
            Species = encounter.Species,
            Form = encounter.Form,
            CurrentLevel = encounter.LevelMin,
            OriginalTrainerFriendship = pi.BaseFriendship,
            MetLocation = encounter switch
            {
                EncounterSlot9a slot => slot.Location,
                ILocation loc => loc.Location,
                _ => (ushort)0
            },
            MetLevel = encounter.LevelMin,
            MetDate = EncounterDate.GetDateSwitch(),
            Version = GameVersion.ZA,
            Ball = (byte)Ball.Poke,
            Nickname = SpeciesName.GetSpeciesNameGeneration(encounter.Species, lang, encounter.Generation),
            ObedienceLevel = encounter.LevelMin,
        };

        // Set trainer info
        // Most Pokemon use player info, but some gifts REQUIRE fixed trainer info for PKHeX validation
        if (encounter is EncounterGift9a gift && gift.Trainer != 0)
        {
            // PKHeX requires these specific trainer details for validation
            pk.OriginalTrainerName = GetFixedTrainerName(gift.Trainer, lang);
            pk.OriginalTrainerGender = GetFixedTrainerGender(gift.Trainer);
            pk.ID32 = GetFixedTrainerID32(gift.Trainer);
        }
        else
        {
            // All other encounters use the player's trainer info
            pk.OriginalTrainerName = tr.OT;
            pk.OriginalTrainerGender = tr.Gender;
            pk.ID32 = tr.ID32;
        }

        // Generate PID, EC, IVs, etc. with our specific seed
        if (!LumioseRNG.GenerateData(pk, param, criteria, seed))
            return null;

        // Set fixed properties from encounter AFTER GenerateData
        // Set Alpha status - check each encounter type since not all implement IAlpha interface
        switch (encounter)
        {
            case EncounterSlot9a slot:
                pk.IsAlpha = slot.IsAlpha;
                break;
            case EncounterStatic9a static9a:
                pk.IsAlpha = static9a.IsAlpha;
                break;
            case EncounterGift9a giftEnc:
                pk.IsAlpha = giftEnc.IsAlpha;
                break;
        }

        if (encounter is IFixedGender fixedGender && fixedGender.Gender != FixedGenderUtil.GenderRandom)
            pk.Gender = fixedGender.Gender;

        if (encounter is IFixedNature fixedNature && fixedNature.Nature != Nature.Random)
            pk.StatNature = pk.Nature = fixedNature.Nature;

        if (encounter is IFixedIVSet fixedIV && fixedIV.IVs.IsSpecified)
        {
            pk.IV_HP = fixedIV.IVs.HP;
            pk.IV_ATK = fixedIV.IVs.ATK;
            pk.IV_DEF = fixedIV.IVs.DEF;
            pk.IV_SPA = fixedIV.IVs.SPA;
            pk.IV_SPD = fixedIV.IVs.SPD;
            pk.IV_SPE = fixedIV.IVs.SPE;
        }

        // Set moves
        SetMoves(pk, encounter, pi);

        // Clear relearn moves - PLZA encounters don't have relearn moves
        pk.SetRelearnMoves(ReadOnlySpan<ushort>.Empty);

        pk.HealPP();
        pk.ResetPartyStats();

        return pk;
    }

    private void SetMoves(PA9 pk, IEncounter9a encounter, PersonalInfo9ZA pi)
    {
        var (learn, plus) = LearnSource9ZA.GetLearnsetAndPlus(encounter.Species, encounter.Form);
        Span<ushort> moves = stackalloc ushort[4];

        // Check if encounter has fixed moves
        if (encounter is IMoveset moveset && moveset.Moves.HasMoves)
        {
            pk.SetMoves(moveset.Moves);
            pk.GetMoves(moves);
            PlusRecordApplicator.SetPlusFlagsEncounter(pk, pi, plus, encounter.LevelMin);
            return;
        }

        // Check if Alpha - handle each encounter type since not all implement IAlpha interface
        bool isAlpha = encounter switch
        {
            EncounterSlot9a slot => slot.IsAlpha,
            EncounterStatic9a static9a => static9a.IsAlpha,
            EncounterGift9a giftEnc => giftEnc.IsAlpha,
            _ => false
        };

        if (!isAlpha)
        {
            learn.SetEncounterMoves(encounter.LevelMin, moves);
            PlusRecordApplicator.SetPlusFlagsEncounter(pk, pi, plus, encounter.LevelMin);
        }
        else
        {
            // Alpha Pokemon get their signature move as the first move
            learn.SetEncounterMovesBackwards(encounter.LevelMin, moves, sameDescend: false);
            moves[0] = pi.AlphaMove;
            pk.SetMoves(moves);

            // Set Plus Move flag for the Alpha Move
            var alphaMove = pi.AlphaMove;
            var indexPlus = PersonalInfo9ZA.PlusMoves.IndexOf(alphaMove);
            if (indexPlus != -1)
                pk.SetMovePlusFlag(indexPlus);

            // Set other Plus Move flags for encounter
            PlusRecordApplicator.SetPlusFlagsEncounter(pk, pi, plus, encounter.LevelMin);
            return;
        }
        pk.SetMoves(moves);
    }

    private static string GetFixedTrainerName(TrainerGift9a trainer, int language) => trainer switch
    {
        TrainerGift9a.Lucario => language switch
        {
            (int)LanguageID.Japanese => "コルニ",
            (int)LanguageID.English => "Korrina",
            (int)LanguageID.French => "Cornélia",
            (int)LanguageID.Italian => "Ornella",
            (int)LanguageID.German => "Connie",
            (int)LanguageID.Spanish => "Corelia",
            (int)LanguageID.Korean => "코르니",
            (int)LanguageID.ChineseS => "可尔妮",
            (int)LanguageID.ChineseT => "可爾妮",
            _ => "Korrina",
        },
        TrainerGift9a.Floette => language switch
        {
            (int)LanguageID.Japanese => "ＡＺ",
            (int)LanguageID.English => "AZ",
            (int)LanguageID.French => "A.Z.",
            (int)LanguageID.Italian => "AZ",
            (int)LanguageID.German => "Azett",
            (int)LanguageID.Spanish => "A. Z.",
            (int)LanguageID.Korean => "AZ",
            (int)LanguageID.ChineseS => "ＡＺ",
            (int)LanguageID.ChineseT => "ＡＺ",
            _ => "AZ",
        },
        TrainerGift9a.Stunfisk => language switch
        {
            (int)LanguageID.Japanese => "グラウン",
            (int)LanguageID.English => "Terri",
            (int)LanguageID.French => "Gad",
            (int)LanguageID.Italian => "Terrence",
            (int)LanguageID.German => "Terry",
            (int)LanguageID.Spanish => "Terry",
            (int)LanguageID.Korean => "그라운",
            (int)LanguageID.ChineseS => "帝尚",
            (int)LanguageID.ChineseT => "帝尚",
            _ => "Terri",
        },
        _ => "Unknown",
    };

    private static byte GetFixedTrainerGender(TrainerGift9a trainer) => trainer switch
    {
        TrainerGift9a.Lucario => 1,
        TrainerGift9a.Floette => 0,
        TrainerGift9a.Stunfisk => 0,
        _ => 0,
    };

    private static uint GetFixedTrainerID32(TrainerGift9a trainer) => trainer switch
    {
        TrainerGift9a.Lucario => 912562,
        TrainerGift9a.Floette => 1,
        TrainerGift9a.Stunfisk => 250932,
        _ => 0,
    };

    /// <summary>
    /// Tries to generate a Pokémon from an encounter and seed.
    /// </summary>
    /// <param name="encounter">Encounter to generate from</param>
    /// <param name="seed">Seed value</param>
    /// <param name="criteria">Generation criteria</param>
    /// <param name="tr">Trainer information</param>
    /// <param name="desiredForm">Desired form</param>
    /// <returns>Generated PA9 if successful, null otherwise</returns>
    private PA9? TryGeneratePokemon(object encounter, ulong seed, EncounterCriteria criteria, SizeType9 sizeType, ITrainerInfo tr, byte desiredForm)
    {
        try
        {
            PA9? pk8 = null;

            // Get PersonalInfo to pass to GetParams
            var pi = encounter switch
            {
                EncounterSlot9a slot => PersonalTable.ZA[slot.Species, slot.Form],
                EncounterStatic9a static9a => PersonalTable.ZA[static9a.Species, static9a.Form],
                EncounterGift9a gift => PersonalTable.ZA[gift.Species, gift.Form],
                EncounterTrade9a trade => PersonalTable.ZA[trade.Species, trade.Form],
                _ => null
            };

            if (pi == null)
                return null;

            // Use each encounter's GetParams method to get the proper parameters with correlation
            var param = encounter switch
            {
                EncounterSlot9a slot => slot.GetParams((PersonalInfo9ZA)pi),
                EncounterStatic9a static9a => static9a.GetParams((PersonalInfo9ZA)pi),
                EncounterGift9a gift => gift.GetParams((PersonalInfo9ZA)pi),
                EncounterTrade9a trade => trade.GetParams((PersonalInfo9ZA)pi),
                _ => default
            };

            // Only override SizeType for non-Alpha encounters
            // Alpha encounters must use SizeType.VALUE with Scale=255
            var isAlpha = encounter switch
            {
                EncounterSlot9a slot => slot.IsAlpha,
                EncounterStatic9a static9a => static9a.IsAlpha,
                EncounterGift9a gift => gift.IsAlpha,
                _ => false
            };

            if (!isAlpha)
            {
                param = param with { SizeType = sizeType };
            }

            // Create Pokemon with our specific seed instead of using ConvertToPKM
            // (ConvertToPKM uses a random seed which creates correlation issues)
            switch (encounter)
            {
                case EncounterSlot9a slot:
                    pk8 = CreatePA9FromEncounter(slot, tr, param, criteria, seed, (PersonalInfo9ZA)pi);
                    break;
                case EncounterStatic9a static9a:
                    pk8 = CreatePA9FromEncounter(static9a, tr, param, criteria, seed, (PersonalInfo9ZA)pi);
                    break;
                case EncounterGift9a gift:
                    pk8 = CreatePA9FromEncounter(gift, tr, param, criteria, seed, (PersonalInfo9ZA)pi);
                    break;
                case EncounterTrade9a trade:
                    pk8 = CreatePA9FromEncounter(trade, tr, param, criteria, seed, (PersonalInfo9ZA)pi);
                    break;
            }

            if (pk8 == null)
                return null;

            var baseForm = encounter switch
            {
                EncounterSlot9a n => n.Form,
                EncounterStatic9a nc => nc.Form,
                EncounterGift9a nd => nd.Form,
                EncounterTrade9a u => u.Form,
                _ => (byte)0
            };

            if (baseForm < EncounterUtil.FormDynamic && pk8.Form != desiredForm)
            {
                return null;
            }

            // Ensure stats are calculated
            pk8.ResetPartyStats();

            return pk8;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if a generated Pokémon matches the search criteria.
    /// </summary>
    /// <param name="pk">Generated Pokémon</param>
    /// <param name="criteria">Search criteria</param>
    /// <param name="ivRanges">IV ranges to check</param>
    /// <param name="scaleRange">Scale range to check</param>
    /// <returns>True if matches criteria, false otherwise</returns>
    private bool CheckPokemonMatchesCriteria(PA9 pk, EncounterCriteria criteria, IVRange[] ivRanges, ScaleRange scaleRange)
    {
        // Check shiny
        bool matchesShiny = criteria.Shiny switch
        {
            Shiny.Never => !pk.IsShiny,
            Shiny.Always => pk.IsShiny,
            Shiny.AlwaysSquare => pk.IsShiny && pk.ShinyXor == 0,
            Shiny.AlwaysStar => pk.IsShiny && pk.ShinyXor > 0 && pk.ShinyXor < 16,
            _ => true
        };

        if (!matchesShiny)
            return false;

        // Check gender
        if (criteria.Gender != Gender.Random && pk.Gender != (int)criteria.Gender)
            return false;

        // Check nature
        if (criteria.Nature != Nature.Random && pk.Nature != criteria.Nature)
            return false;

        // Check IVs
        if (!CheckIVRanges(pk, ivRanges))
            return false;

        // Check ability
        if (criteria.Ability != AbilityPermission.Any12H && !CheckAbilityCriteria(pk, criteria.Ability))
            return false;

        // Check scale
        if (pk.Scale < scaleRange.Min || pk.Scale > scaleRange.Max)
            return false;

        return true;
    }

    /// <summary>
    /// Checks if a Pokémon's ability matches the specified criteria.
    /// </summary>
    /// <param name="pk">Pokémon to check</param>
    /// <param name="criteria">Ability criteria</param>
    /// <returns>True if ability matches, false otherwise</returns>
    private static bool CheckAbilityCriteria(PA9 pk, AbilityPermission criteria)
    {
        var pi = PersonalTable.ZA[pk.Species, pk.Form];

        return (criteria, pk.AbilityNumber) switch
        {
            (AbilityPermission.OnlyFirst, 1) => pk.Ability == pi.Ability1,
            (AbilityPermission.OnlySecond, 2) => pk.Ability == pi.Ability2,
            (AbilityPermission.OnlyHidden, 4) => pk.Ability == pi.AbilityH,
            (AbilityPermission.Any12, 1) => pk.Ability == pi.Ability1,
            (AbilityPermission.Any12, 2) => pk.Ability == pi.Ability2,
            (_, _) when criteria == AbilityPermission.Any12H => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if a Pokémon's IVs are within the specified ranges.
    /// </summary>
    /// <param name="pk">Pokémon to check</param>
    /// <param name="ranges">IV ranges to validate against</param>
    /// <returns>True if all IVs are within range, false otherwise</returns>
    private static bool CheckIVRanges(PA9 pk, IVRange[] ranges)
    {
        return pk.IV_HP >= ranges[0].Min && pk.IV_HP <= ranges[0].Max &&
               pk.IV_ATK >= ranges[1].Min && pk.IV_ATK <= ranges[1].Max &&
               pk.IV_DEF >= ranges[2].Min && pk.IV_DEF <= ranges[2].Max &&
               pk.IV_SPA >= ranges[3].Min && pk.IV_SPA <= ranges[3].Max &&
               pk.IV_SPD >= ranges[4].Min && pk.IV_SPD <= ranges[4].Max &&
               pk.IV_SPE >= ranges[5].Min && pk.IV_SPE <= ranges[5].Max;
    }

    /// <summary>
    /// Adds a seed result to the results grid.
    /// </summary>
    /// <param name="result">Seed result to add</param>
    private void AddResultToGrid(SeedResult result)
    {
        try
        {
            this.Invoke(() =>
            {
                if (!IsDisposed && !resultsGrid.IsDisposed)
                {
                    var wrapper = new EncounterWrapper(result.Encounter, GameVersion.ZA);
                    var stars = GetStarRating(result.Encounter);
                    var shinyType = result.Pokemon.IsShiny ? (result.Pokemon.ShinyXor == 0 ? "■" : "★") : "";

                    var row = resultsGrid.Rows.Add(
                        $"{result.Seed:X16}",
                        wrapper.GetShortDescription(),
                        stars,
                        shinyType,
                        result.Pokemon.Nature.ToString(),
                        GetAbilityName(result.Pokemon),
                        GetIVString(result.Pokemon),
                        result.Pokemon.Scale.ToString(),
                        result.Pokemon.IsAlpha ? "Yes" : "No"
                    );

                    // Store the result index in the row tag for easy retrieval
                    resultsGrid.Rows[row].Tag = result;

                    if (result.Pokemon.IsShiny)
                    {
                        var isSquare = result.Pokemon.ShinyXor == 0;
                        resultsGrid.Rows[row].DefaultCellStyle.BackColor = isSquare ? Color.FromArgb(32, 32, 64) : Color.FromArgb(64, 64, 32);
                        resultsGrid.Rows[row].DefaultCellStyle.ForeColor = isSquare ? Color.DeepSkyBlue : Color.Gold;
                        resultsGrid.Rows[row].DefaultCellStyle.SelectionBackColor = isSquare ? Color.DarkBlue : Color.DarkGoldenrod;
                        resultsGrid.Rows[row].DefaultCellStyle.SelectionForeColor = Color.White;
                    }
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // Form was disposed while updating, ignore
        }
    }

    /// <summary>
    /// Gets the star rating (flawless IV count) for an encounter.
    /// </summary>
    /// <param name="encounter">Encounter to check</param>
    /// <returns>Star rating string</returns>
    private static string GetStarRating(object encounter)
    {
        return encounter switch
        {
            EncounterSlot9a n => $"{n.FlawlessIVCount}IV",
            EncounterGift9a nd => $"{nd.FlawlessIVCount}IV",
            EncounterStatic9a nc => $"{nc.FlawlessIVCount}IV",
            EncounterTrade9a u => $"{u.FlawlessIVCount}IV",
            _ => "?"
        };
    }

    /// <summary>
    /// Gets the ability name for a Pokémon.
    /// </summary>
    /// <param name="pk">Pokémon to check</param>
    /// <returns>Ability name string</returns>
    private static string GetAbilityName(PA9 pk)
    {
        var abilities = PersonalTable.ZA[pk.Species, pk.Form];
        return pk.AbilityNumber switch
        {
            1 => GameInfo.Strings.abilitylist[abilities.Ability1],
            2 => GameInfo.Strings.abilitylist[abilities.Ability2],
            4 => GameInfo.Strings.abilitylist[abilities.AbilityH],
            _ => "?"
        };
    }

    /// <summary>
    /// Gets a formatted IV string for a Pokémon.
    /// </summary>
    /// <param name="pk">Pokémon to check</param>
    /// <returns>Formatted IV string</returns>
    private static string GetIVString(PA9 pk)
    {
        return $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
    }

    /// <summary>
    /// Handles double-click events on the results grid to load Pokémon into the editor.
    /// Temporarily enables SearchShiny1 during load for proper validation, then disables it to prevent PKHeX slowdown.
    /// </summary>
    private async void ResultsGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= resultsGrid.Rows.Count)
            return;

        var result = resultsGrid.Rows[e.RowIndex].Tag as SeedResult;
        if (result == null)
            return;

        try
        {
            // Enable SearchShiny1 temporarily so PKHeX validates the Pokemon correctly
            // This is especially important for Shiny Alphas which need PID+ correlation validation
            LumioseSolver.SearchShiny1 = true;

            // Small delay to ensure PKHeX recognizes the SearchShiny1 setting change
            await Task.Delay(50);

            // Load the Pokémon into PKHeX editor
            _pkmEditor.PopulateFields(result.Pokemon);

            var wrapper = new EncounterWrapper(result.Encounter, GameVersion.ZA);
            WinFormsUtil.Alert($"Loaded {result.Pokemon.Nickname}!\nSeed: {result.Seed:X16}\nEncounter: {wrapper.GetDescription()}");

            // Wait for PKHeX to complete validation
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error($"Failed to load Pokémon: {ex.Message}");
        }
        finally
        {
            // Always disable SearchShiny1 after loading to prevent PKHeX from slowing down
            // when validating boxes (it would run expensive PID+ checks on every Pokemon)
            LumioseSolver.SearchShiny1 = false;
        }
    }

    /// <summary>
    /// Handles the export button click to save results to CSV.
    /// </summary>
    private void ExportButton_Click(object? sender, EventArgs e)
    {
        if (_results.Count == 0)
        {
            WinFormsUtil.Alert("No results to export!");
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"PLZA_Seeds_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (sfd.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            using var writer = new System.IO.StreamWriter(sfd.FileName);
            writer.WriteLine($"Seed,Encounter,Stars,Shiny,Nature,Ability,IVs,Alpha,EC,PID,TID,SID");

            foreach (var result in _results)
            {
                var wrapper = new EncounterWrapper(result.Encounter, GameVersion.ZA);
                var stars = GetStarRating(result.Encounter);
                var shinyType = result.Pokemon.IsShiny ? (result.Pokemon.ShinyXor == 0 ? "Square" : "Star") : "No";

                // Convert to display format (Gen 7+ format)
                uint pokemonId32 = ((uint)result.Pokemon.SID16 << 16) | result.Pokemon.TID16;
                uint displayTID = pokemonId32 % 1000000;
                uint displaySID = pokemonId32 / 1000000;

                writer.WriteLine($"{result.Seed:X16},{wrapper.GetDescription()},{stars},{shinyType}," +
                               $"{result.Pokemon.Nature},{GetAbilityName(result.Pokemon)},{GetIVString(result.Pokemon)}," +
                               $"{(result.Pokemon.IsAlpha ? "Yes" : "No")},{result.Pokemon.EncryptionConstant:X8}," +
                               $"{result.Pokemon.PID:X8},{displayTID},{displaySID}");
            }

            WinFormsUtil.Alert("Export successful!");
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error($"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles source checkbox changes to update encounter lists.
    /// </summary>
    private void SourceCheckChanged(object? sender, EventArgs e)
    {
        if (speciesCombo.SelectedValue is int species)
        {
            UpdateEncounterList(species);
            UpdateSourceDisplay();
            UpdateEncounterCombo();
        }
    }

    /// <summary>
    /// DEBUG: Test Tyrunt gift encounter generation and validation
    /// </summary>
    private void DebugTyruntEncounter()
    {
        string logPath = "";
        try
        {
            // Create log file in current directory with timestamp
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logPath = Path.Combine(Directory.GetCurrentDirectory(), $"PLZASeedFinder_Debug_{timestamp}.txt");

            using var writer = new StreamWriter(logPath, false);

            writer.WriteLine("=".PadRight(80, '='));
            writer.WriteLine("PLZA Seed Finder - Tyrunt Gift Encounter Debug Log");
            writer.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine("=".PadRight(80, '='));
            writer.WriteLine();

            // Find Tyrunt (species 696) gift encounters using existing method
            var tyruntGifts = GetGiftEncounters(696, GameVersion.ZA);

            writer.WriteLine($"Found {tyruntGifts.Count} Tyrunt gift encounters");
            writer.WriteLine();

            if (tyruntGifts.Count == 0)
            {
                writer.WriteLine("No Tyrunt gift encounters found. Exiting.");
                return;
            }

            // Test with each Tyrunt encounter
            int encounterNum = 0;
            foreach (var gift in tyruntGifts)
            {
                encounterNum++;
                writer.WriteLine("-".PadRight(80, '-'));
                writer.WriteLine($"Encounter #{encounterNum}");
                writer.WriteLine("-".PadRight(80, '-'));
                writer.WriteLine();

                writer.WriteLine("Encounter Properties:");
                writer.WriteLine($"  Level: {gift.Level}");
                writer.WriteLine($"  Location: {gift.Location}");
                writer.WriteLine($"  FlawlessIVCount: {gift.FlawlessIVCount}");
                writer.WriteLine($"  Shiny: {gift.Shiny}");
                writer.WriteLine($"  IsAlpha: {gift.IsAlpha}");
                writer.WriteLine($"  Trainer: {gift.Trainer}");
                writer.WriteLine($"  Fixed Moves: {(gift.Moves.HasMoves ? $"{gift.Moves.Move1}, {gift.Moves.Move2}, {gift.Moves.Move3}, {gift.Moves.Move4}" : "None")}");
                writer.WriteLine();

                // Get trainer info from save file
                var sav = _saveFileEditor.SAV;
                var tr = sav;

                writer.WriteLine("Save File Trainer Info:");
                writer.WriteLine($"  OT: {tr.OT}");
                writer.WriteLine($"  TID: {tr.DisplayTID}");
                writer.WriteLine($"  SID: {tr.DisplaySID}");
                writer.WriteLine($"  Gender: {tr.Gender}");
                writer.WriteLine();

                // Get PersonalInfo
                var pi = PersonalTable.ZA[gift.Species, gift.Form];

                // Get parameters from the encounter
                var param = gift.GetParams((PersonalInfo9ZA)pi);

                writer.WriteLine("Generation Parameters:");
                writer.WriteLine($"  Correlation: {param.Correlation}");
                writer.WriteLine($"  FlawlessIVs: {param.FlawlessIVs}");
                writer.WriteLine();

                // Test with a few different seeds
                ulong[] testSeeds = { 0x0000000000000001, 0x1234567890ABCDEF, 0xFFFFFFFFFFFFFFFF };

                foreach (var testSeed in testSeeds)
                {
                    writer.WriteLine($"Testing Seed: 0x{testSeed:X16}");
                    writer.WriteLine();

                    // Create criteria
                    var criteria = EncounterCriteria.Unrestricted;

                    // Generate the Pokemon
                    var pk = CreatePA9FromEncounter(gift, tr, param, criteria, testSeed, (PersonalInfo9ZA)pi);

                    if (pk == null)
                    {
                        writer.WriteLine($"  ERROR: Failed to create PA9 from encounter");
                        writer.WriteLine();
                        continue;
                    }

                    writer.WriteLine("Generated Pokemon:");
                    writer.WriteLine($"  Species: {pk.Species} ({SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English)})");
                    writer.WriteLine($"  Form: {pk.Form}");
                    writer.WriteLine($"  Level: {pk.CurrentLevel}");
                    writer.WriteLine($"  MetLevel: {pk.MetLevel}");
                    writer.WriteLine($"  MetLocation: {pk.MetLocation}");
                    writer.WriteLine($"  OT: {pk.OriginalTrainerName}");
                    writer.WriteLine($"  TID: {pk.DisplayTID}");
                    writer.WriteLine($"  SID: {pk.DisplaySID}");
                    writer.WriteLine($"  Gender: {pk.Gender}");
                    writer.WriteLine($"  Nature: {pk.Nature}");
                    writer.WriteLine($"  Ability: {pk.Ability}");
                    writer.WriteLine($"  IVs: {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}");
                    writer.WriteLine($"  PID: 0x{pk.PID:X8}");
                    writer.WriteLine($"  EC: 0x{pk.EncryptionConstant:X8}");
                    writer.WriteLine($"  IsShiny: {pk.IsShiny}");
                    writer.WriteLine($"  ShinyXor: {pk.ShinyXor}");
                    writer.WriteLine($"  IsAlpha: {pk.IsAlpha}");
                    writer.WriteLine($"  Ball: {pk.Ball}");
                    writer.WriteLine($"  Version: {pk.Version}");
                    writer.WriteLine($"  Language: {pk.Language}");
                    writer.WriteLine($"  Moves: {pk.Move1}, {pk.Move2}, {pk.Move3}, {pk.Move4}");
                    writer.WriteLine($"  ObedienceLevel: {pk.ObedienceLevel}");
                    writer.WriteLine();

                    // Validate with PKHeX
                    var la = new LegalityAnalysis(pk);

                    writer.WriteLine($"Validation Result: {(la.Valid ? "VALID" : "INVALID")}");
                    writer.WriteLine();

                    if (!la.Valid)
                    {
                        writer.WriteLine("Validation Errors:");
                        foreach (var error in la.Results.Where(r => !r.Valid))
                        {
                            writer.WriteLine($"  - [{error.Judgement}] {error.Identifier}");
                        }
                        writer.WriteLine();
                    }

                    // Only test first seed for first encounter to avoid spam
                    break;
                }

                writer.WriteLine();
            }

            writer.WriteLine("=".PadRight(80, '='));
            writer.WriteLine("Debug log complete");
            writer.WriteLine("=".PadRight(80, '='));

            WinFormsUtil.Alert($"Debug log saved to:\n{logPath}");
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(logPath, $"Error during debug: {ex.Message}\n\n{ex.StackTrace}");
            }
            catch
            {
                // If we can't write to file, show error
            }
            WinFormsUtil.Error($"Error during debug: {ex.Message}\n\nLog path: {logPath}");
        }
    }

    /// <summary>
    /// Represents a seed search result.
    /// </summary>
    private class SeedResult
    {
        /// <summary>
        /// The seed value that generated this result.
        /// </summary>
        public ulong Seed { get; set; }

        /// <summary>
        /// The encounter that was used for generation.
        /// </summary>
        public object Encounter { get; set; } = null!;

        /// <summary>
        /// The generated Pokémon.
        /// </summary>
        public PA9 Pokemon { get; set; } = null!;
    }

    /// <summary>
    /// Combo box item for display.
    /// </summary>
    /// <param name="text">Display text</param>
    /// <param name="value">Associated value</param>
    private class ComboItem(string text, int value)
    {
        /// <summary>
        /// Display text for the item.
        /// </summary>
        public string Text { get; } = text;

        /// <summary>
        /// Associated value for the item.
        /// </summary>
        public int Value { get; } = value;
    }

    /// <summary>
    /// Wrapper for encounter objects with version information.
    /// </summary>
    private class EncounterWrapper
    {
        /// <summary>
        /// The wrapped encounter object.
        /// </summary>
        public object Encounter { get; }

        /// <summary>
        /// The game version for this encounter.
        /// </summary>
        public GameVersion Version { get; }

        /// <summary>
        /// Initializes a new instance of the EncounterWrapper class.
        /// </summary>
        /// <param name="encounter">Encounter to wrap</param>
        /// <param name="version">Game version</param>
        public EncounterWrapper(object encounter, GameVersion version)
        {
            Encounter = encounter;
            Version = version;
        }

        /// <summary>
        /// Gets the form of the wrapped encounter.
        /// </summary>
        public byte Form => Encounter switch
        {
            EncounterSlot9a n => n.Form,
            EncounterStatic9a nc => nc.Form,
            EncounterGift9a nd => nd.Form,
            EncounterTrade9a u => u.Form,
            _ => 0
        };

        /// <summary>
        /// Gets a full description of the encounter.
        /// </summary>
        /// <returns>Description string</returns>
        public string GetDescription()
        {
            return Encounter switch
            {
                EncounterSlot9a slot => $"Wild - Lv.{slot.LevelMin}{(slot.LevelMax != slot.LevelMin ? $"-{slot.LevelMax}" : "")} {(slot.IsAlpha ? "(Alpha)" : "")}",
                EncounterStatic9a static9a => $"Static - Lv.{static9a.Level} {(static9a.IsAlpha ? "(Alpha)" : "")}",
                EncounterGift9a gift => $"Gift - Lv.{gift.Level}",
                EncounterTrade9a trade => $"Trade - Lv.{trade.Level}",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Gets a short description of the encounter.
        /// </summary>
        /// <returns>Short description string</returns>
        public string GetShortDescription()
        {
            return Encounter switch
            {
                EncounterSlot9a => "Wild",
                EncounterStatic9a => "Static",
                EncounterGift9a => "Gift",
                EncounterTrade9a => "Trade",
                _ => "?"
            };
        }

        /// <summary>
        /// Gets the version string for display.
        /// </summary>
        /// <returns>Version string</returns>
        private string GetVersionString()
        {
            return Version switch
            {
                GameVersion.ZA => "ZA",
                _ => ""
            };
        }
    }
}
