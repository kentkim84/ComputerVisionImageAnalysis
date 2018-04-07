﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace ComputerVision
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // Microsoft cognitive service - Computer Vision
        // The subscriptionKey string key and the uri base have to be in the same region
        private const string accesskeyCV = "4667d551d2504931b6cd71ffdea1118e";
        private const string accessKeyBing = "a84ffb2e1cb04405b880896c239b998a";
        private const string uriBaseCV = "https://westeurope.api.cognitive.microsoft.com/vision/v1.0/analyze";
        private const string uriBaseBing = "https://api.cognitive.microsoft.com/bing/v7.0/images/search";

        // MediaCapture and its state variables
        private MediaCapture _mediaCapture;
        private VideoEncodingProperties _previewProperties;
        private bool _isPreviewing;

        // Prevent the screen from sleeping while the camera is running
        private readonly DisplayRequest _displayRequest = new DisplayRequest();

        // Display size        
        private int _videoFrameWidth;
        private int _videoFrameHeight;

        // Bitmap holder and Image stream
        private SoftwareBitmap _softwareBitmap;
        private WriteableBitmap _imgSource;

        // Image byte array
        private byte[] _byteData;

        // Used to return image search results including relevant headers
        struct SearchResult
        {
            public String jsonResult;
            public Dictionary<String, String> relevantHeaders;
        }

        #region Constructor, lifecycle and navigation

        public MainPage()
        {
            this.InitializeComponent();
            this.ViewModel = new ImageInfoViewModel();
            Application.Current.Suspending += Application_Suspending;
        }
        public ImageInfoViewModel ViewModel { get; set; }
        private async void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            // Handle global application events only if this page is active
            if (Frame.CurrentSourcePageType == typeof(MainPage))
            {
                var deferral = e.SuspendingOperation.GetDeferral();
                await InitialiseCameraAsync();
                deferral.Complete();
            }
        }
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            await InitialiseCameraAsync();
        }

        #endregion Constructor, lifecycle and navigation



        #region Event handlers

        private async void CameraButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await InitialiseCameraAsync();

            // Visibility change
            cameraGrid.Visibility = Visibility.Visible;
            cropGrid.Visibility = Visibility.Collapsed;
            processConfirmButton.Visibility = Visibility.Collapsed;
            processCancelButton.Visibility = Visibility.Collapsed;
        }
        private async void FileButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var picker = new FileOpenPicker()
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                FileTypeFilter = { ".jpg", ".jpeg", ".png" },
            };

            var file = await picker.PickSingleFileAsync();

            if (file != null)
            {
                await CleanupPreviewAndBitmapAsync();
                await LoadImageAsync(file);

                // Visibility change
                cameraGrid.Visibility = Visibility.Collapsed;
                cropGrid.Visibility = Visibility.Visible;
                processConfirmButton.Visibility = Visibility.Visible;
                processCancelButton.Visibility = Visibility.Visible;
            }
        }
        private async void PhotoButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await CaptureImageAsync();

            // Visibility change
            cameraGrid.Visibility = Visibility.Collapsed;
            cropGrid.Visibility = Visibility.Visible;
            processConfirmButton.Visibility = Visibility.Visible;
            processCancelButton.Visibility = Visibility.Visible;
        }
        private async void processConfirmButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Start managing image source
            // Get image analysis as string
            var imageAnalysis = await MakeAnalysisRequest(_byteData);
            var searchResult = BingImageSearch(imageAnalysis);

            // 
            ProcessSearchResult(searchResult);

            // Change visibility
            processConfirmButton.Visibility = Visibility.Collapsed;
            processCancelButton.Visibility = Visibility.Collapsed;
        }
        private async void processCancelButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Debug.WriteLine("Process Cancelled");
            await CleanupPreviewAndBitmapAsync();
        }        
        private void ImageInfoGridView_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Move to the detail page
            //this.Frame.Navigate(typeof(Page1));
        }
        private void Rect_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            //rectangle.Opacity = 0.5;
        }
        private void Rect_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            //, PointerRoutedEventArgs e2
            //_pt = e2.GetCurrentPoint(this);
            // translate
            //rectTransform.TranslateX += e.Delta.Translation.X;
            //rectTransform.TranslateY += e.Delta.Translation.Y;
            // scale
            //rectTransform.ScaleX += 0.01;
            //rectTransform.ScaleY += 0.01;
        }
        private void Rect_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            //rectangle.Opacity = 0.3;
        }

        #endregion Event handlers



        #region MediaCapture methods

        private async Task InitialiseCameraAsync()
        {
            await CleanupPreviewAndBitmapAsync();

            if (_mediaCapture == null)
            {
                await StartPreviewAsync();
            }

        }
        // Starts the preview and adjusts it for for rotation and mirroring after making a request to keep the screen on
        private async Task StartPreviewAsync()
        {
            try
            {
                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings { });
                var previewResolution = _mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview);
                var photoResolution = _mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.Photo);

                VideoEncodingProperties allResolutionsAvailable;
                uint height, width;
                //use debugger at the following line to check height & width for video preview resolution
                for (int i = 0; i < previewResolution.Count; i++)
                {
                    allResolutionsAvailable = previewResolution[i] as VideoEncodingProperties;
                    height = allResolutionsAvailable.Height;
                    width = allResolutionsAvailable.Width;
                }
                //use debugger at the following line to check height & width for captured photo resolution
                for (int i = 0; i < photoResolution.Count; i++)
                {
                    allResolutionsAvailable = photoResolution[i] as VideoEncodingProperties;
                    height = allResolutionsAvailable.Height;
                    width = allResolutionsAvailable.Width;
                }

                // Prevent the device from sleeping while previewing
                _displayRequest.RequestActive();
                DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape;

                // Video Preview resolution 8-th Height: 480, Width: 640
                // Captured Photo resolution 8-th Height: 480, Width: 640
                var selectedPreviewResolution = _mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview).ElementAt(8);
                var selectedPhotoResolution = _mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.Photo).ElementAt(8);

                await _mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, selectedPreviewResolution);
                await _mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.Photo, selectedPhotoResolution);

                // Get information about the current preview
                _previewProperties = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

                // Set the camera preview and the size
                previewControl.Source = _mediaCapture;
                _videoFrameHeight = (int)_previewProperties.Height;
                _videoFrameWidth = (int)_previewProperties.Width;

                // Set the root grid size as previewing size
                //ApplicationView.GetForCurrentView().TryResizeView(new Size
                //{
                //    Height = _videoFrameHeight + commandBarPanel.ActualHeight,
                //    Width = _videoFrameWidth
                //});

                await _mediaCapture.StartPreviewAsync();

                _isPreviewing = true;
            }
            catch (UnauthorizedAccessException)
            {
                // This will be thrown if the user denied access to the camera in privacy settings
                Debug.WriteLine("The app was denied access to the camera");
                return;
            }
            catch (System.IO.FileLoadException)
            {
                _mediaCapture.CaptureDeviceExclusiveControlStatusChanged += _mediaCapture_CaptureDeviceExclusiveControlStatusChanged;
            }

        }
        private async Task CleanupPreviewAndBitmapAsync()
        {
            if (_mediaCapture != null)
            {
                if (_isPreviewing)
                {
                    await _mediaCapture.StopPreviewAsync();
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Cleanup the UI
                    previewControl.Source = null;
                    if (_displayRequest != null)
                    {
                        // Allow the device screen to sleep now that the preview is stopped
                        _displayRequest.RequestRelease();
                    }

                    // Cleanup the media capture
                    _mediaCapture.Dispose();
                    _mediaCapture = null;
                });
            }

            if (_softwareBitmap != null)
            {
                _softwareBitmap.Dispose();
                _softwareBitmap = null;
            }
        }
        private async Task CaptureImageAsync()
        {
            // Create the video frame to request a SoftwareBitmap preview frame.
            var videoFrame = new VideoFrame(BitmapPixelFormat.Bgra8, _videoFrameWidth, _videoFrameHeight);

            // Capture the preview frame.
            using (var currentFrame = await _mediaCapture.GetPreviewFrameAsync(videoFrame))
            {
                // Create softwarebitmap from current video frame
                _softwareBitmap = currentFrame.SoftwareBitmap;
                // Set image source
                await SetImageControlSource(_softwareBitmap);
            }
        }
        private async Task LoadImageAsync(StorageFile file)
        {
            using (var fileStream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read))
            {
                var decoder = await BitmapDecoder.CreateAsync(fileStream);
                // Create software bitmap from file stream
                _softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                // Set image source
                await SetImageControlSource(_softwareBitmap);
            }
        }

        #endregion MediaCapture methods



        #region Helper functions

        private async Task SetImageControlSource(SoftwareBitmap softwareBitmap)
        {
            // Get byte array from software bitmap
            _byteData = await EncodedBytes(softwareBitmap, BitmapEncoder.JpegEncoderId);
            // Create writeable bitmap from software bitmap
            _imgSource = new WriteableBitmap(softwareBitmap.PixelWidth, softwareBitmap.PixelHeight);
            // Copy software bitmap buffer to writeable bitmap
            softwareBitmap.CopyToBuffer(_imgSource.PixelBuffer);
            // Set UI control source
            imageControl.Source = _imgSource;
        }
        private async Task<byte[]> EncodedBytes(SoftwareBitmap soft, Guid encoderId)
        {
            byte[] array = null;

            // First: Use an encoder to copy from SoftwareBitmap to an in-mem stream (FlushAsync)
            // Next:  Use ReadAsync on the in-mem stream to get byte[] array

            using (var ms = new InMemoryRandomAccessStream())
            {
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(encoderId, ms);
                encoder.SetSoftwareBitmap(soft);

                try
                {
                    await encoder.FlushAsync();
                }
                catch (Exception ex) { return new byte[0]; }

                array = new byte[ms.Size];
                await ms.ReadAsync(array.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
            }
            return array;
        }
        private async void _mediaCapture_CaptureDeviceExclusiveControlStatusChanged(MediaCapture sender, MediaCaptureDeviceExclusiveControlStatusChangedEventArgs args)
        {
            if (args.Status == MediaCaptureDeviceExclusiveControlStatus.SharedReadOnlyAvailable)
            {
                Debug.WriteLine("The camera preview can't be displayed because another app has exclusive access");
            }
            else if (args.Status == MediaCaptureDeviceExclusiveControlStatus.ExclusiveControlAvailable && !_isPreviewing)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    await StartPreviewAsync();
                });
            }
        }
        // Gets the analysis of the specified image file by using the Computer Vision REST API.
        private static async Task<string> MakeAnalysisRequest(byte[] _byteData)
        {
            HttpClient client = new HttpClient();

            // Request headers.
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", accesskeyCV);

            // Request parameters. A third optional parameter is "details".
            string requestParameters = "visualFeatures=Categories,Description,Color&language=en";

            // Assemble the URI for the REST API Call.
            string uri = uriBaseCV + "?" + requestParameters;

            HttpResponseMessage response;

            using (ByteArrayContent content = new ByteArrayContent(_byteData))
            {
                // This example uses content type "application/octet-stream".
                // The other content types you can use are "application/json" and "multipart/form-data".
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                // Execute the REST API call.
                response = await client.PostAsync(uri, content);

                // Get the JSON response.
                var contentString = await response.Content.ReadAsStringAsync();

                // Display the JSON response.
                //Debug.WriteLine("\nResponse:\n");
                //Debug.WriteLine(JsonPrettyPrintCV(contentString));                

                return ProcessJsonContent(contentString);
            }
        }
        private static string ProcessJsonContent(string jsonContent)
        {
            if (string.IsNullOrEmpty(jsonContent))
                return string.Empty;

            // Get tags and captions
            JObject jObject = JObject.Parse(jsonContent);
            IList<JToken> jArray = jObject["description"]["captions"].Children().ToList();
            var result = (string)jArray[0]["text"];

            Debug.WriteLine(result);
            return result;
        }
        // Performs a Bing Image search and return the results as a SearchResult.        
        private string BingImageSearch(string searchQuery)
        {
            // Construct the URI of the search request
            var uriQuery = uriBaseBing + "?q=" + Uri.EscapeDataString(searchQuery);

            // Perform the Web request and get the response
            WebRequest request = HttpWebRequest.Create(uriQuery);
            request.Headers["Ocp-Apim-Subscription-Key"] = accessKeyBing;
            HttpWebResponse response = (HttpWebResponse)request.GetResponseAsync().Result;
            string json = new StreamReader(response.GetResponseStream()).ReadToEnd();

            // Create result object for return
            var searchResult = new SearchResult()
            {
                jsonResult = json,
                relevantHeaders = new Dictionary<String, String>()
            };

            // Extract Bing HTTP headers
            foreach (String header in response.Headers)
            {
                if (header.StartsWith("BingAPIs-") || header.StartsWith("X-MSEdge-"))
                    searchResult.relevantHeaders[header] = response.Headers[header];
            }

            Debug.WriteLine("\nRelevant HTTP Headers:\n");
            foreach (var header in searchResult.relevantHeaders)
                Debug.WriteLine(header.Key + ": " + header.Value);

            //Debug.WriteLine("\nJSON Response:\n");
            //Debug.WriteLine(JsonPrettyPrintBing(searchResult.jsonResult));

            return searchResult.jsonResult;
        }
        private void ProcessSearchResult(string searchResult)
        {
            JObject jObject = JObject.Parse(searchResult);
            IList<JToken> jArray = jObject["value"].Children().ToList();
            List<ImageInfo> imageResourceList = new List<ImageInfo>();

            // Retrieve elements from the value
            foreach (JToken t in jArray)
            {
                var thumbnailJObject = (JObject)t["thumbnail"];

                var thumbnail = new Thumbnail()
                {
                    width = (int)thumbnailJObject["width"],
                    height = (int)thumbnailJObject["height"]
                };

                var imageResource = new ImageInfo()
                {
                    Name = (string)t["name"],
                    ThumbnailUrl = (string)t["thumbnailUrl"],
                    ContentUrl = (string)t["contentUrl"],
                    HostPageUrl = (string)t["hostPageUrl"],
                    Width = (int)t["width"],
                    Height = (int)t["height"],
                    Thumbnail = thumbnail
                };

                // Add item into List
                imageResourceList.Add(imageResource);
            }

            // Add image resource list to items source
            this.ViewModel = new ImageInfoViewModel(imageResourceList);
            ImageInfoGridView.ItemsSource = ViewModel.ImageInfoCVS;
        }
        // Formats the given JSON string by adding line breaks and indents.
        private static string JsonPrettyPrintCV(string json)
        {
            if (string.IsNullOrEmpty(json))
                return string.Empty;

            json = json.Replace(Environment.NewLine, "").Replace("\t", "");

            string INDENT_STRING = "    ";
            var indent = 0;
            var quoted = false;
            var sb = new StringBuilder();
            for (var i = 0; i < json.Length; i++)
            {
                var ch = json[i];
                switch (ch)
                {
                    case '{':
                    case '[':
                        sb.Append(ch);
                        if (!quoted)
                        {
                            sb.AppendLine();
                            Enumerable.Range(0, ++indent).ForEach(item => sb.Append(INDENT_STRING));
                        }
                        break;
                    case '}':
                    case ']':
                        if (!quoted)
                        {
                            sb.AppendLine();
                            Enumerable.Range(0, --indent).ForEach(item => sb.Append(INDENT_STRING));
                        }
                        sb.Append(ch);
                        break;
                    case '"':
                        sb.Append(ch);
                        bool escaped = false;
                        var index = i;
                        while (index > 0 && json[--index] == '\\')
                            escaped = !escaped;
                        if (!escaped)
                            quoted = !quoted;
                        break;
                    case ',':
                        sb.Append(ch);
                        if (!quoted)
                        {
                            sb.AppendLine();
                            Enumerable.Range(0, indent).ForEach(item => sb.Append(INDENT_STRING));
                        }
                        break;
                    case ':':
                        sb.Append(ch);
                        if (!quoted)
                            sb.Append(" ");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
        private static string JsonPrettyPrintBing(string json)
        {
            if (string.IsNullOrEmpty(json))
                return string.Empty;

            json = json.Replace(Environment.NewLine, "").Replace("\t", "");

            StringBuilder sb = new StringBuilder();
            bool quote = false;
            bool ignore = false;
            char last = ' ';
            int offset = 0;
            int indentLength = 2;

            foreach (char ch in json)
            {
                switch (ch)
                {
                    case '"':
                        if (!ignore) quote = !quote;
                        break;
                    case '\\':
                        if (quote && last != '\\') ignore = true;
                        break;
                }

                if (quote)
                {
                    sb.Append(ch);
                    if (last == '\\' && ignore) ignore = false;
                }
                else
                {
                    switch (ch)
                    {
                        case '{':
                        case '[':
                            sb.Append(ch);
                            sb.Append(Environment.NewLine);
                            sb.Append(new string(' ', ++offset * indentLength));
                            break;
                        case '}':
                        case ']':
                            sb.Append(Environment.NewLine);
                            sb.Append(new string(' ', --offset * indentLength));
                            sb.Append(ch);
                            break;
                        case ',':
                            sb.Append(ch);
                            sb.Append(Environment.NewLine);
                            sb.Append(new string(' ', offset * indentLength));
                            break;
                        case ':':
                            sb.Append(ch);
                            sb.Append(' ');
                            break;
                        default:
                            if (quote || ch != ' ') sb.Append(ch);
                            break;
                    }
                }
                last = ch;
            }

            return sb.ToString().Trim();
        }


        #endregion Helper functions


    }
    public static class Extensions
    {
        public static void ForEach<T>(this IEnumerable<T> ie, Action<T> action)
        {
            foreach (var i in ie)
            {
                action(i);
            }
        }
    }
    public class ImageInfo
    {
        // Auto-Impl Properties for trivial get and set
        public string Name { get; set; }
        public string ThumbnailUrl { get; set; }
        public string ContentUrl { get; set; }
        public string HostPageUrl { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Size { get { return Width + " x " + Height; } }
        public Thumbnail Thumbnail { get; set; }
    }
    public class ImageInfoViewModel
    {
        private ObservableCollection<ImageInfo> imageInfoCVS = new ObservableCollection<ImageInfo>();
    
        // Default Constructor
        public ImageInfoViewModel()
        {
            var defaultCount = 12;
            for (int i = 0; i < defaultCount; i++)
            {
                this.imageInfoCVS.Add(new ImageInfo()
                {
                    Name = "Default Box",
                    ThumbnailUrl = @"Assets\Square150x150Logo.scale-200.png"
                });
            }
        }
        // Add each imageResource into observable collection
        public ImageInfoViewModel(List<ImageInfo> imageResourceList)
        {
            foreach (ImageInfo imageResource in imageResourceList)
            {
                this.imageInfoCVS.Add(imageResource);
            }
        }
        
        public ObservableCollection<ImageInfo> ImageInfoCVS { get { return imageInfoCVS; } }
    }
    public class Thumbnail
    {
        public int width { get; set; }
        public int height { get; set; }
    }
}
