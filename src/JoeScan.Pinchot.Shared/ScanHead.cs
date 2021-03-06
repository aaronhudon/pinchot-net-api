﻿// Copyright(c) JoeScan Inc. All Rights Reserved.
//
// Licensed under the BSD 3 Clause License. See LICENSE.txt in the project
// root for license information.

using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace JoeScan.Pinchot
{
    /// <summary>
    /// An interface to a physical scan head.
    /// </summary>
    /// <remarks>
    /// The <see cref="ScanHead"/> class provides an interface to a physical scan head by providing properties
    /// and methods for configuration, status retrieval, and profile data retrieval. A <see cref="ScanHead"/>
    /// must belong to a <see cref="ScanSystem"/> and is created using <see cref="ScanSystem.CreateScanHead"/>.
    /// </remarks>
    public class ScanHead : IDisposable
    {
        #region Private Fields

        private ScanSystem scanSystem;
        private ScanHeadSenderReceiver senderReceiver;
        private bool disposed;

        #endregion

        #region Events

        internal event EventHandler<CommStatsEventArgs> StatsEvent;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the serial number of the physical scan head. The value is set when
        /// the <see cref="ScanHead"/> is created using <see cref="ScanSystem.CreateScanHead"/>.
        /// </summary>
        /// <value>The serial number of the physical scan head.</value>
        public uint SerialNumber { get; }

        /// <summary>
        /// Gets the unique ID of the <see cref="ScanHead"/>. The value is set when
        /// the <see cref="ScanHead"/> is created using <see cref="ScanSystem.CreateScanHead"/>.
        /// </summary>
        /// <value>The unique ID of the <see cref="ScanHead"/>.</value>
        public uint ID { get; }

        /// <summary>
        /// Gets the <see cref="System.Net.IPAddress"/> of the physical scan head. The value
        /// is determined automatically via network discovery according to the <see cref="SerialNumber"/>.
        /// </summary>
        /// <value>The <see cref="System.Net.IPAddress"/> of the physical scan head.</value>
        [JsonIgnore]
        public IPAddress IPAddress { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the network connection to the physical scan head is established.
        /// </summary>
        /// <value>A value indicating whether the network connection to the physical scan head is established.</value>
        [JsonIgnore]
        public bool IsConnected => senderReceiver != null && senderReceiver.IsConnected;

        /// <summary>
        /// Gets the most recent <see cref="ScanHeadStatus"/> received from the physical scan head.
        /// </summary>
        /// <remarks>Status messages are not sent from the scan head while scanning.</remarks>
        /// <value>The most recent <see cref="ScanHeadStatus"/> received from the physical scan head.</value>
        [JsonIgnore]
        public ScanHeadStatus Status { get; internal set; }

        /// <summary>
        /// Gets the number of <see cref="Profile"/>s available in the local buffer for the <see cref="ScanHead"/>.
        /// </summary>
        /// <remarks>
        /// All existing <see cref="Profile"/>s are cleared from the local buffer when <see cref="ScanSystem.StartScanning"/>
        /// is called successfully.
        /// </remarks>
        /// <value>The number of <see cref="Profile"/>s available in the local buffer for the <see cref="ScanHead"/>.</value>
        [JsonIgnore]
        public int NumberOfProfilesAvailable => Profiles.Count;

        /// <summary>
        /// Gets a value indicating whether the <see cref="ScanHead"/> profile buffer overflowed.
        /// </summary>
        /// <remarks>Resets to `false` when <see cref="ScanSystem.StartScanning"/> is called successfully.</remarks>
        /// <value>A value indicating whether the <see cref="ScanHead"/> profile buffer overflowed.</value>
        [JsonIgnore]
        public bool ProfileBufferOverflowed => senderReceiver.ProfileBufferOverflowed;

        #endregion

        #region Internal Properties

        [JsonProperty(nameof(Enabled))]
        internal bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets the <see cref="ScanHeadConfiguration"/> used to configure the physical scan head. Use
        /// <see cref="Configure"/> to set.
        /// </summary>
        /// <seealso cref="Configure"/>
        /// <value>The <see cref="ScanHeadConfiguration"/> used to configure the physical scan head.</value>
        [JsonProperty(nameof(Configuration))]
        internal ScanHeadConfiguration Configuration { get; private set; } = new ScanHeadConfiguration();

        /// <summary>
        /// Network communication statistics for the scan head.
        /// </summary>
        internal CommStatsEventArgs CommStats { get; private set; } = new CommStatsEventArgs();

        /// <summary>
        /// Enable / disable network communication statistics.
        /// </summary>
        internal bool CommStatsEnabled => scanSystem.CommStatsEnabled;

        /// <summary>
        /// Gets the thread safe collection of profiles received from the scan head.
        /// </summary>
        internal BlockingCollection<Profile> Profiles { get; } =
            new BlockingCollection<Profile>(new ConcurrentQueue<Profile>(), Globals.ProfileBufferSize);

        /// <summary>
        /// Gets the spatial transformation parameters of the scan head required to
        /// transform the data from a camera based coordinate system to one based on
        /// mill placement. Read Only. Use SetAlignment to set all alignment values.
        /// </summary>
        /// <seealso cref="SetAlignment(double, double, double, ScanHeadOrientation)"/> or 
        /// <seealso cref="SetAlignment(Camera, double, double, double, ScanHeadOrientation)"/>
        [JsonProperty(nameof(Alignment))]
        internal Dictionary<Camera, AlignmentParameters> Alignment { get; private set; } =
            new Dictionary<Camera, AlignmentParameters>()
            {
                { Camera.Camera0, new AlignmentParameters(0, 0, 0, ScanHeadOrientation.CableIsUpstream) },
                { Camera.Camera1, new AlignmentParameters(0, 0, 0, ScanHeadOrientation.CableIsUpstream) }
            };

        /// <summary>
        /// Gets the <see cref="ScanWindow"/> within which a camera will look for the laser.
        /// Use <see cref="SetWindow(ScanWindow)"/> to set.
        /// </summary>
        /// <seealso cref="SetWindow(ScanWindow)"/>
        [JsonProperty(nameof(Window))]
        internal ScanWindow Window { get; private set; } = ScanWindow.CreateScanWindowUnconstrained();

        internal long CompleteProfilesReceivedCount => senderReceiver.CompleteProfilesReceivedCount;

        internal long IncompleteProfilesReceivedCount => senderReceiver.IncompleteProfilesReceivedCount;

        internal long BadPacketsCount => senderReceiver.BadPacketsCount;

        internal IPEndPoint ClientIpEndPoint => senderReceiver.LocalReceiveIpEndPoint;

        internal bool IsVersionMismatched => senderReceiver.IsVersionMismatched;

        internal string VersionMismatchReason => senderReceiver.VersionMismatchReason;

        #endregion

        #region Lifecycle

        internal ScanHead(ScanSystem scanSystem, uint serialNumber, uint id)
        {
            this.scanSystem = scanSystem;
            SerialNumber = serialNumber;
            ID = id;
        }

        [JsonConstructor]
        internal ScanHead(uint serialNumber, uint id)
        {
            SerialNumber = serialNumber;
            ID = id;
        }

        /// <nodoc/>
        ~ScanHead()
        {
            Dispose(false);
        }

        /// <summary>
        /// Releases the managed and unmanaged resources used by the <see cref="ScanHead"/>
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="ScanHead"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">Whether being disposed explicitly (true) or due to a finalizer (false).</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                senderReceiver?.Dispose();
                Profiles?.Dispose();
            }

            disposed = true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Configure the physical scan head according to the provided <see cref="ScanHeadConfiguration"/> parameter.
        /// </summary>
        /// <remarks>
        /// The <see cref="ScanHeadConfiguration"/> parameters are only sent to the scan head when <see cref="ScanSystem.StartScanning"/> is called. <br/>
        /// A clone of <paramref name="configuration"/> is created and assigned to <see cref="Configuration"/>, so a reference to a single
        /// <see cref="ScanHeadConfiguration"/> cannot be shared amongst <see cref="ScanHead"/>s.
        /// </remarks>
        /// <param name="configuration">The <see cref="ScanHeadConfiguration"/> to use for configuration
        /// of the physical scan head.</param>
        /// <exception cref="Exception">
        /// <see cref="ScanSystem.IsScanning"/> is `true`.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> is `null`.
        /// </exception>
        public void Configure(ScanHeadConfiguration configuration)
        {
            if (scanSystem.IsScanning)
            {
                throw new Exception("Can not set configuration while scanning.");
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            // we make a copy so that we don't share a single configuration between all heads
            Configuration = (ScanHeadConfiguration)configuration.Clone();
        }

        /// <summary>
        /// Gets a clone of the <see cref="ScanHeadConfiguration"/> used to configure the physical scan head. Use
        /// <see cref="Configure"/> to set the <see cref="ScanHeadConfiguration"/>.
        /// </summary>
        /// <value>A clone of the <see cref="ScanHeadConfiguration"/> used to configure the physical scan head.</value>
        /// <exception cref="NullReferenceException">The <see cref="ScanHeadConfiguration"/> to be cloned is null.</exception>
        public ScanHeadConfiguration GetConfigurationClone()
        {
            if (Configuration is null)
            {
                throw new NullReferenceException("Configuration is null.");
            }

            return (ScanHeadConfiguration)Configuration.Clone();
        }

        /// <summary>
        /// Removes a <see cref="Profile"/>.
        /// </summary>
        /// <returns>The <see cref="Profile"/> removed.</returns>
        public Profile TakeNextProfile()
        {
            return Profiles.Take();
        }

        /// <summary>
        /// Removes a <see cref="Profile"/> while observing a <see cref="CancellationToken"/>.
        /// </summary>
        /// <param name="token"><see cref="CancellationToken"/> to observe.</param>
        /// <returns>The <see cref="Profile"/> removed.</returns>
        public Profile TakeNextProfile(CancellationToken token)
        {
            return Profiles.Take(token);
        }

        /// <summary>
        /// Tries to remove a <see cref="Profile"/>.
        /// </summary>
        /// <param name="profile">The <see cref="Profile"/> to be removed.</param>
        /// <returns>Whether a <see cref="Profile"/> was successfully taken.</returns>
        public bool TryTakeNextProfile(out Profile profile)
        {
            return Profiles.TryTake(out profile);
        }

        /// <summary>
        /// Tries to remove a <see cref="Profile"/> in the specified time period while
        /// observing a <see cref="CancellationToken"/>.
        /// </summary>
        /// <param name="profile">The <see cref="Profile"/> to be removed.</param>
        /// <param name="timeout">A <see cref="TimeSpan"/> that represents the time to wait,
        /// or a <see cref="TimeSpan"/> that represents -1 milliseconds to wait indefinitely.</param>
        /// <param name="token"><see cref="CancellationToken"/> to observe.</param>
        /// <returns>Whether a <see cref="Profile"/> was successfully taken.</returns>
        public bool TryTakeNextProfile(out Profile profile, TimeSpan timeout, CancellationToken token)
        {
            return Profiles.TryTake(out profile, (int)timeout.TotalMilliseconds, token);
        }

        /// <summary>
        /// Sets the spatial transform parameters of the <see cref="ScanHead"/> in order to properly
        /// transform the data from a scan head based coordinate system to one based on
        /// mill placement. Parameters are applied to all <see cref="Camera"/>s.
        /// </summary>
        /// <param name="rollDegrees">The rotation around the Z axis in the mill coordinate system in degrees.</param>
        /// <param name="shiftX">The shift along the X axis in the mill coordinate system in inches.</param>
        /// <param name="shiftY">The shift along the Y axis in the mill coordinate system in inches.</param>
        /// <param name="orientation">The <see cref="ScanHeadOrientation"/>.</param>
        /// <exception cref="Exception">
        /// <see cref="IsConnected"/> is `true`.
        /// </exception>
        public void SetAlignment(double rollDegrees, double shiftX, double shiftY, ScanHeadOrientation orientation)
        {
            if (IsConnected)
            {
                throw new Exception("Can not set alignment while connected.");
            }

            Alignment[Camera.Camera0] = new AlignmentParameters(rollDegrees, shiftX, shiftY, orientation);
            Alignment[Camera.Camera1] = new AlignmentParameters(rollDegrees, shiftX, shiftY, orientation);
        }

        /// <summary>
        /// Sets the spatial transform parameters of the <see cref="ScanHead"/> in order to properly
        /// transform the data from a scan head based coordinate system to one based on
        /// mill placement. Parameters are applied only to specified <see cref="Camera"/>. This method
        /// should not be used in most cases as all cameras should be aligned to each other by factory
        /// calibration. In most cases <see cref="SetAlignment(double,double,double,ScanHeadOrientation)"/> should be
        /// used instead.
        /// </summary>
        /// <param name="camera">The <see cref="Camera"/> which to set the alignment of.</param>
        /// <param name="rollDegrees">The rotation around the Z axis in the mill coordinate system in degrees.</param>
        /// <param name="shiftX">The shift along the X axis in the mill coordinate system in inches.</param>
        /// <param name="shiftY">The shift along the Y axis in the mill coordinate system in inches.</param>
        /// <param name="orientation">The <see cref="ScanHeadOrientation"/>.</param>
        /// <exception cref="Exception">
        /// <see cref="IsConnected"/> is `true`.
        /// </exception>
        public void SetAlignment(Camera camera, double rollDegrees, double shiftX, double shiftY,
            ScanHeadOrientation orientation)
        {
            if (IsConnected)
            {
                throw new Exception("Can not set alignment while connected.");
            }

            Alignment[camera] = new AlignmentParameters(rollDegrees, shiftX, shiftY, orientation);
        }

        /// <summary>
        /// Sets the <see cref="ScanWindow"/>, in mill coordinates, within which a camera will look for the laser. Default is an
        /// unconstrained window.
        /// </summary>
        /// <remarks>
        /// The <see cref="ScanWindow"/> constraints are only sent to the scan head when <see cref="ScanSystem.Connect"/> is called.
        /// </remarks>
        /// <param name="window">The <see cref="ScanWindow"/> to use for the <see cref="ScanHead"/>.</param>
        /// <exception cref="Exception">
        /// <see cref="IsConnected"/> is `true`.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="window"/> is null.
        /// </exception>
        public void SetWindow(ScanWindow window)
        {
            if (IsConnected)
            {
                throw new Exception("Can not set scan window while connected.");
            }

            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            Window = (ScanWindow)window.Clone();
        }

        /// <summary>
        /// Captures an image from the specified <see cref="Camera"/>.
        /// </summary>
        /// <param name="camera">The <see cref="Camera"/> from which to capture the image.</param>
        /// <param name="enableLasers">A value indicating whether to enable the <see cref="Laser"/>(s)
        /// during the image capture.</param>
        /// <returns>The <see cref="CameraImage"/>.</returns>
        public CameraImage GetCameraImage(Camera camera, bool enableLasers)
        {
            if (!IsConnected)
            {
                var msg = "Not connected.";
                throw new Exception(msg);
            }

            if (scanSystem.IsScanning)
            {
                var msg = "Scan system is scanning.";
                throw new Exception(msg);
            }

            var images = GetCameraImages(1, enableLasers, CancellationToken.None);
            return images[camera].First();
        }

        #endregion

        #region Internal Methods

        internal void StartSenderReceiver(byte sessionId, ConnectionType connType = ConnectionType.Normal)
        {
            if (!Enabled) return;

            senderReceiver?.Dispose();
            senderReceiver = new ScanHeadSenderReceiver(this, CommStatsEnabled);
            senderReceiver.Start(sessionId, connType);
            senderReceiver.StatsEvent += RaiseCommStatsUpdateEvent;
        }

        internal void StartScanning(double scanRate, AllDataFormat dataFormat)
        {
            if (!Enabled) return;

            if (scanRate > Status.MaxScanRate)
            {
                throw new ArgumentException(
                    $"Requested scan rate ({scanRate}Hz) is greater than that allowed by the " +
                    $"current scan head configuration ({Status.MaxScanRate}Hz) for scan head {ID}.");
            }

            ClearProfiles();
            senderReceiver.StartScanning(scanRate, dataFormat, scanSystem.StartColumn, scanSystem.EndColumn);
        }

        internal void StopScanning()
        {
            if (!Enabled) return;

            senderReceiver.ClearScanRequests();
        }

        internal void SetWindow()
        {
            if (!Enabled) return;

            senderReceiver.SetWindow();
        }

        internal void Disconnect()
        {
            if (!Enabled) return;

            senderReceiver.Disconnect();
            senderReceiver.Stop();
        }

        /// <summary>
        /// Performs a validation of the scan head's configuration to ensure there
        /// are no conflicts. Note, this method is not currently implemented.
        /// </summary>
        /// <returns><c>true</c> if the configuration is valid, <c>false</c> otherwise.</returns>
        /// <exception cref="NullReferenceException">
        /// <see cref="Configuration"/> is `null`.
        /// </exception>
        internal bool ValidateConfiguration()
        {
            if (Configuration is null)
            {
                throw new NullReferenceException("Scan head configuration is null.");
            }

            return Configuration.Validate();
        }

        /// <summary>
        /// Sets the spatial transform parameters of the scan head in order to properly
        /// transform the data from a camera based coordinate system to one based on
        /// mill placement. Parameters are applied to all cameras.
        /// </summary>
        /// <param name="p">The p.</param>
        internal void SetAlignment(AlignmentParameters p)
        {
            if (IsConnected)
            {
                throw new Exception("Can not set alignment while connected.");
            }

            Alignment[Camera.Camera0] = new AlignmentParameters(p);
            Alignment[Camera.Camera1] = new AlignmentParameters(p);
        }

        // Should only ever be used when loading scan heads from file (JSON deserialization)
        internal void SetScanSystem(ScanSystem scanSystem)
        {
            this.scanSystem = scanSystem;
        }

        internal IDictionary<Camera, IList<CameraImage>> GetCameraImages(int numberOfImages, bool enableLasers,
            CancellationToken token)
        {
            var images = new Dictionary<Camera, IList<CameraImage>>();
            for (var camera = Camera.Camera0; (int)camera < Status.NumValidCameras; camera++)
            {
                images.Add(camera, new List<CameraImage>());
            }

            if (!IsConnected)
            {
                var msg = "Not connected.";
                throw new Exception(msg);
            }

            if (scanSystem.IsScanning)
            {
                var msg = "Scan system is scanning.";
                throw new Exception(msg);
            }

            var userConfig = GetConfigurationClone();

            if (!enableLasers)
            {
                Configuration.SetLaserOnTime(15.0, 15.0, 15.0);
            }
            else
            {
                var laserMin = Configuration.MinLaserOnTime;
                var laserDefault = Configuration.DefaultLaserOnTime;
                var laserMax = Configuration.MaxLaserOnTime;

                if (laserMin > Configuration.MinCameraExposureTime)
                {
                    laserMin = Configuration.MinCameraExposureTime;
                }

                if (laserDefault > Configuration.DefaultCameraExposureTime)
                {
                    laserDefault = Configuration.DefaultCameraExposureTime;
                }

                if (laserMax > Configuration.MinCameraExposureTime)
                {
                    laserMax = Configuration.MaxCameraExposureTime;
                }

                Configuration.SetLaserOnTime(laserMin, laserDefault, laserMax);
            }

            var rateHz = 1.0 / (Status.NumValidCameras * Configuration.MaxCameraExposureTime * 1e-6);
            if (rateHz > 2)
            {
                rateHz = 2;
            }

            StartScanning(rateHz, AllDataFormat.Image);

            try
            {
                while (images.Any(keyValuePair => keyValuePair.Value.Count < numberOfImages))
                {
                    var profile = TakeNextProfile(token);

                    if (images[profile.Camera].Count >= numberOfImages) continue;

                    var image = new CameraImage()
                    {
                        ScanHeadID = profile.ScanHeadID,
                        Camera = profile.Camera,
                        DataFormat = profile.DataFormat,
                        EncoderValues = profile.EncoderValues,
                        Laser = profile.Laser,
                        LaserOnTime = profile.LaserOnTime,
                        ExposureTime = profile.ExposureTime,
                        Data = profile.Image,
                        Width = Globals.CameraImageDataMaxWidth,
                        Height = Globals.CameraImageDataMaxHeight,
                        Timestamp = profile.Timestamp
                    };

                    images[profile.Camera].Add(image);
                }
            }
            catch (OperationCanceledException)
            {
                StopScanning();
                Configure(userConfig);
                throw new OperationCanceledException();
            }

            StopScanning();
            Configure(userConfig);

            return images;
        }

        internal ScanHeadTemperatureSensors GetTemperatureData()
        {
            var restClient = new RestClient($"http://{IPAddress}:8080/sensors");
            restClient.UseJson();
            var request = new RestRequest("temperature");
            request.AddHeader("Content-Type", "application/json");
            var response = restClient.Execute(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"REST request to {IPAddress} was unsuccessful. {response.ErrorMessage}");
            }

            return JsonConvert.DeserializeObject<ScanHeadTemperatureSensors>(response.Content);
        }

        internal ScanHeadPowerSensors GetPowerData()
        {
            var restClient = new RestClient($"http://{IPAddress}:8080/sensors");
            restClient.UseJson();
            var request = new RestRequest("power");
            request.AddHeader("Content-Type", "application/json");
            var response = restClient.Execute(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"REST request to {IPAddress} was unsuccessful. {response.ErrorMessage}");
            }

            return JsonConvert.DeserializeObject<ScanHeadPowerSensors>(response.Content);
        }

        internal ScanHeadChannelAlignment GetChannelAlignmentData()
        {
            var restClient = new RestClient($"http://{IPAddress}:8080/tests");
            restClient.UseJson();
            var request = new RestRequest("channel-alignment");
            request.AddHeader("Content-Type", "application/json");
            var response = restClient.Execute(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"REST request to {IPAddress} was unsuccessful. {response.ErrorMessage}");
            }

            return JsonConvert.DeserializeObject<ScanHeadChannelAlignment>(response.Content);
        }

        internal ScanHeadLaserCameraExposureTimes GetExposureTimes()
        {
            var restClient = new RestClient($"http://{IPAddress}:8080/config");
            restClient.UseJson();
            var request = new RestRequest("exposure-times");
            request.AddHeader("Content-Type", "application/json");
            var response = restClient.Execute(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"REST request to {IPAddress} was unsuccessful. {response.ErrorMessage}");
            }

            return JsonConvert.DeserializeObject<ScanHeadLaserCameraExposureTimes>(response.Content);
        }

        internal void SetCameraLaserExposureTimes(uint cameraStart, uint cameraEnd, uint laserStart, uint laserEnd)
        {
            var restClient = new RestClient($"http://{IPAddress}:8080/config");
            restClient.UseJson();
            var request = new RestRequest("exposure-times");
            request.Method = Method.POST;
            request.AddHeader("Content-Type", "application/json");
            request.AddJsonBody(new { cameraStart, cameraEnd, laserStart, laserEnd });
            var response = restClient.Execute(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"REST request to {IPAddress} was unsuccessful. {response.ErrorMessage}");
            }
        }

        #endregion

        #region Private Methods

        private void RaiseCommStatsUpdateEvent(object sender, CommStatsEventArgs args)
        {
            CommStats = args;
            StatsEvent?.Invoke(this, new CommStatsEventArgs()
            {
                ID = CommStats.ID,
                CompleteProfilesReceived = CommStats.CompleteProfilesReceived,
                ProfileRate = CommStats.ProfileRate,
                BytesReceived = CommStats.BytesReceived,
                Evicted = CommStats.Evicted,
                DataRate = CommStats.DataRate,
                BadPackets = CommStats.BadPackets,
                GoodPackets = CommStats.GoodPackets
            });
        }

        private void ClearProfiles()
        {
            while (Profiles.TryTake(out var _))
            {
            }
        }

        #endregion
    }
}