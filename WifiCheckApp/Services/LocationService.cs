using Microsoft.Maui.Devices.Sensors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WifiCheckApp.Services
{
    public class LocationService
    {
        private readonly IGeolocation _geolocation;
        private readonly IPreferences _preferences;
        private const string LocationLatKey = "CompanyLocationLat";
        private const string LocationLongKey = "CompanyLocationLong";
        private const string LocationSetKey = "IsCompanyLocationSet";
        private const double MaxDistanceInMeters = 30; // Maximum allowed distance in meters

        // Cache for location data
        private Location _lastKnownLocation;
        private DateTime _lastLocationTime = DateTime.MinValue;
        private const int LOCATION_CACHE_VALID_SECONDS = 15; // Cache location for 15 seconds
        private SemaphoreSlim _locationSemaphore = new SemaphoreSlim(1, 1);
        private double _cachedDistance = -1;
        private DateTime _lastDistanceTime = DateTime.MinValue;

        public LocationService(IGeolocation geolocation, IPreferences preferences)
        {
            _geolocation = geolocation;
            _preferences = preferences;
        }

        public bool IsLocationSet()
        {
            return _preferences.Get(LocationSetKey, false);
        }

        public void SaveCompanyLocation()
        {
            try
            {
                _preferences.Set(LocationLatKey, "21.03044508896132");
                _preferences.Set(LocationLongKey, "105.76408102932089");
                _preferences.Set(LocationSetKey, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving company location: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> IsWithinCompanyRange()
        {
            if (!IsLocationSet())
            {
                return false;
            }

            try
            {
                double distance = await GetDistanceFromCompany();
                return distance >= 0 && distance <= MaxDistanceInMeters;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking location range: {ex.Message}");
                return false;
            }
        }

        public async Task<Location> GetCurrentLocation()
        {
            // Check if we have a recent cached location
            if (_lastKnownLocation != null &&
                (DateTime.Now - _lastLocationTime).TotalSeconds < LOCATION_CACHE_VALID_SECONDS)
            {
                return _lastKnownLocation;
            }

            try
            {
                // Use semaphore to prevent multiple parallel location requests
                if (!await _locationSemaphore.WaitAsync(0))
                {
                    // Another location request is in progress, return cached location or null
                    return _lastKnownLocation;
                }

                // Use a shorter timeout for GPS
                var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(5));
                var location = await _geolocation.GetLocationAsync(request);

                if (location != null)
                {
                    _lastKnownLocation = location;
                    _lastLocationTime = DateTime.Now;
                }

                return location;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting current location: {ex.Message}");
                return null;
            }
            finally
            {
                if (_locationSemaphore.CurrentCount == 0)
                {
                    _locationSemaphore.Release();
                }
            }
        }

        public async Task<double> GetDistanceFromCompany()
        {
            if (!IsLocationSet())
            {
                return -1;
            }

            // Check if we have a cached distance that's still valid
            if (_cachedDistance >= 0 &&
                (DateTime.Now - _lastDistanceTime).TotalSeconds < LOCATION_CACHE_VALID_SECONDS)
            {
                return _cachedDistance;
            }

            try
            {
                var currentLocation = await GetCurrentLocation();
                if (currentLocation == null)
                {
                    return -1;
                }

                var companyLocation = new Location(
                    _preferences.Get(LocationLatKey, 0.0),
                    _preferences.Get(LocationLongKey, 0.0)
                );

                // Calculate distance between current location and company location
                double distance = Location.CalculateDistance(
                    currentLocation,
                    companyLocation,
                    DistanceUnits.Kilometers) * 1000; // Convert to meters

                // Cache the distance
                _cachedDistance = distance;
                _lastDistanceTime = DateTime.Now;

                return distance;
            }
            catch
            {
                return -1;
            }
        }

        public void ClearCompanyLocation()
        {
            _preferences.Remove(LocationLatKey);
            _preferences.Remove(LocationLongKey);
            _preferences.Remove(LocationSetKey);
        }
    }
}