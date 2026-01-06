using DeadEditor.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace DeadEditor;

public partial class LibraryBrowserWindow : Window
{
    private readonly string _libraryRoot;
    private readonly MetadataService _metadataService;
    private readonly MainWindow _mainWindow;
    private List<LibraryShow> _shows = new();

    public LibraryBrowserWindow(string libraryRoot, MainWindow mainWindow)
    {
        InitializeComponent();
        _libraryRoot = libraryRoot;
        _mainWindow = mainWindow;
        _metadataService = new MetadataService();
        LibraryPathText.Text = libraryRoot;
        LoadShows();
    }

    private void LoadShows()
    {
        _shows.Clear();

        if (!Directory.Exists(_libraryRoot))
        {
            MessageBox.Show($"Library directory does not exist: {_libraryRoot}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Scan year folders
        var yearFolders = Directory.GetDirectories(_libraryRoot);

        foreach (var yearFolder in yearFolders)
        {
            var showFolders = Directory.GetDirectories(yearFolder);

            foreach (var showFolder in showFolders)
            {
                var folderName = Path.GetFileName(showFolder);

                // Expected format: "yyyy-MM-dd - Venue, City, State"
                var parts = folderName.Split(new[] { " - " }, 2, System.StringSplitOptions.None);

                if (parts.Length == 2)
                {
                    var date = parts[0];
                    var venueParts = parts[1].Split(new[] { ", " }, System.StringSplitOptions.None);

                    var venue = venueParts.Length > 0 ? venueParts[0] : "";
                    var city = venueParts.Length > 1 ? venueParts[1] : "";
                    var state = venueParts.Length > 2 ? venueParts[2] : "";

                    var flacFiles = Directory.GetFiles(showFolder, "*.flac");

                    _shows.Add(new LibraryShow
                    {
                        Date = date,
                        Venue = venue,
                        City = city,
                        State = state,
                        Location = venueParts.Length > 1 ? string.Join(", ", venueParts.Skip(1)) : "",
                        TrackCount = flacFiles.Length,
                        FolderPath = showFolder
                    });
                }
            }
        }

        // Sort by date descending
        _shows = _shows.OrderByDescending(s => s.Date).ToList();

        ShowsDataGrid.ItemsSource = _shows;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShowsDataGrid.SelectedItem is LibraryShow show)
        {
            if (Directory.Exists(show.FolderPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", show.FolderPath);
            }
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadShows();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LoadShowButton_Click(object sender, RoutedEventArgs e)
    {
        LoadSelectedShow();
    }

    private void ShowsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        LoadSelectedShow();
    }

    private void LoadSelectedShow()
    {
        if (ShowsDataGrid.SelectedItem is LibraryShow show)
        {
            // Load the show into the main window
            _mainWindow.LoadShowFromLibrary(show.FolderPath);

            // Close this browser window
            Close();
        }
    }

    private void ShowsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShowsDataGrid.SelectedItem is LibraryShow show)
        {
            LoadTracks(show);
        }
        else
        {
            NoSelectionText.Visibility = Visibility.Visible;
            TracksDataGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadTracks(LibraryShow show)
    {
        try
        {
            // Read tracks from the show folder
            var tracks = _metadataService.ReadFolder(show.FolderPath);

            if (tracks.Count > 0)
            {
                TracksDataGrid.ItemsSource = tracks;
                NoSelectionText.Visibility = Visibility.Collapsed;
                TracksDataGrid.Visibility = Visibility.Visible;
            }
            else
            {
                NoSelectionText.Text = "No tracks found";
                NoSelectionText.Visibility = Visibility.Visible;
                TracksDataGrid.Visibility = Visibility.Collapsed;
            }
        }
        catch (System.Exception ex)
        {
            NoSelectionText.Text = $"Error loading tracks: {ex.Message}";
            NoSelectionText.Visibility = Visibility.Visible;
            TracksDataGrid.Visibility = Visibility.Collapsed;
        }
    }
}

public class LibraryShow
{
    public string Date { get; set; } = "";
    public string Venue { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Location { get; set; } = "";
    public int TrackCount { get; set; }
    public string FolderPath { get; set; } = "";
}
