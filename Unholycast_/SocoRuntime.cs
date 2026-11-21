using Python.Runtime;

namespace Unholycast_
{
    public record class SonosDeviceInfo
    {
        public required string Ip { get; init; }
        public required string Name { get; init; }
        public required string CoordinatorIp { get; init; }
        public bool IsCoordinator { get; init; }
        public int Volume { get; init; }
        public bool Muted { get; init; }
    }

    public record class SonosGroupInfo
    {
        public required string CoordinatorIp { get; init; }
        public required string CoordinatorName { get; init; }
        public List<string> Members { get; init; } = new();
    }

    public record struct Volume
    {
        public int value;
        public bool muted;
    }
    public record class SonosTransportState
    {
        public string? current_transport_state;
        public string? current_transport_status;
        public string? current_transport_speed;
    }
    public record class SonosTrackInfo
    {
        public string? title;
        public int position;
        public int duration;
    }
    public class SocoRuntime
    {
        public dynamic soco;
        public SocoRuntime()
        {
            using (Py.GIL())
            {
                dynamic socoModule = Py.Import("soco");
                soco = socoModule;
            }
        }
        public string FindDevice(string nameOrIp)
        {
            using (Py.GIL())
            {
                if (System.Net.IPAddress.TryParse(nameOrIp, out _))
                    return nameOrIp;

                dynamic all = soco.discovery.discover();
                if (all == null)
                    return string.Empty;

                foreach (dynamic dev in all)
                {
                    string pname = (string)dev.player_name;
                    if (string.Equals(pname, nameOrIp, StringComparison.OrdinalIgnoreCase))
                        return dev.ip_address;
                }
            }
            return string.Empty;
        }

        public List<SonosDeviceInfo> DiscoverDevices()
        {
            var devices = new List<SonosDeviceInfo>();

            using (Py.GIL())
            {
                dynamic all = soco.discovery.discover();
                if (all == null)
                    return devices;

                foreach (dynamic dev in all)
                {
                    try
                    {
                        string ip = (string)dev.ip_address;
                        string name = (string)dev.player_name;
                        int volume = (int)dev.volume;
                        bool muted = (bool)dev.mute;

                        string coordinatorIp = ip;
                        try
                        {
                            coordinatorIp = (string)dev.group.coordinator.ip_address;
                        }
                        catch
                        {
                            coordinatorIp = ip;
                        }

                        devices.Add(new SonosDeviceInfo
                        {
                            Ip = ip,
                            Name = name,
                            CoordinatorIp = coordinatorIp,
                            IsCoordinator = string.Equals(ip, coordinatorIp, StringComparison.OrdinalIgnoreCase),
                            Volume = volume,
                            Muted = muted
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RUNTIME] Could not map discovered device: {ex.Message}");
                    }
                }
            }

            return devices;
        }

        public List<SonosGroupInfo> DiscoverGroups()
        {
            var devices = DiscoverDevices();
            var groups = devices
                .GroupBy(d => d.CoordinatorIp, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var coordinator = g.FirstOrDefault(x => x.IsCoordinator) ?? g.First();
                    return new SonosGroupInfo
                    {
                        CoordinatorIp = g.Key,
                        CoordinatorName = coordinator.Name,
                        Members = g.Select(x => x.Ip).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    };
                })
                .ToList();

            return groups;
        }

        public bool JoinGroup(string memberIp, string coordinatorIp)
        {
            try
            {
                using (Py.GIL())
                {
                    dynamic member = soco.SoCo(memberIp);
                    dynamic coordinator = soco.SoCo(coordinatorIp);
                    member.join(coordinator);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RUNTIME] Join group failed ({memberIp} -> {coordinatorIp}): {ex.Message}");
                return false;
            }
        }

        public bool RemoveFromGroup(string memberIp)
        {
            try
            {
                using (Py.GIL())
                {
                    dynamic member = soco.SoCo(memberIp);
                    member.unjoin();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RUNTIME] Unjoin failed ({memberIp}): {ex.Message}");
                return false;
            }
        }

        public bool SetVolumeByIp(string ip, Volume volume)
        {
            try
            {
                using (Py.GIL())
                {
                    dynamic device = soco.SoCo(ip);
                    device.volume = volume.value;
                    device.mute = volume.muted;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RUNTIME] Setting volume failed ({ip}): {ex.Message}");
                return false;
            }
        }

        public Volume GetVolumeByIp(string ip)
        {
            using (Py.GIL())
            {
                dynamic device = soco.SoCo(ip);
                return new Volume
                {
                    value = (int)device.volume,
                    muted = (bool)device.mute
                };
            }
        }
        public dynamic GetDevice(string ip)
        {
            using (Py.GIL())
            {
                dynamic dev = soco.SoCo(ip);
                return dev;
            }
        }
        public int GetPosition(dynamic device)
        {
            using (Py.GIL())
            {
                dynamic ts = device.get_current_track_info();
                return ts["position"];
            }
        }
        public SonosTrackInfo GetSonosTrackInfo(dynamic device)
        {
            try
            {
                var retval = new SonosTrackInfo();
                string dur = "";
                string pos = "";
                using (Py.GIL())
                {
                    dynamic ts = device.get_current_track_info();
                    retval.title = ts["title"];
                    dur = ts["duration"];
                    pos = ts["position"];
                }
                var dd = dur.Split(':');
                var pp = pos.Split(":");
                retval.duration = int.Parse(dd[0]) * 60 * 60 + int.Parse(dd[1]) * 60 + int.Parse(dd[2]);
                retval.position = int.Parse(pp[0]) * 60 * 60 + int.Parse(pp[1]) * 60 + int.Parse(pp[2]);
                return retval;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return new()
            {
                duration = 0,
            };
        }
        public SonosTransportState GetTransportState(dynamic device)
        {
            try
            {
                var retval = new SonosTransportState();
                //dynamic _s;
                using (Py.GIL())
                {
                    dynamic ts = device.get_current_transport_info();
                    retval.current_transport_state = ts["current_transport_state"];
                    retval.current_transport_status = ts["current_transport_status"];
                    //_s = ts["current_speed"];
                }
                //retval.current_transport_speed = 
                return retval;
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[RUNTIME] Something went wrong when getting transport info {ex.Message}");
            }
            return new()
            {
                current_transport_state = "STOPPED",
                current_transport_status = "ERROR"
            };
        }
        public Volume GetVolume(dynamic device) {
            Volume v;
            using (Py.GIL())
            {
                v.value = device.volume;
                v.muted = device.mute;
            }
            return v;
        }
        public void SetVolume(dynamic device,Volume volume)
        {
            using (Py.GIL())
            {
                device.volume =volume.value;
                device.mute = volume.muted;
            }
        }
        public void PlayUrl(dynamic device,string url, string meta) {
            using (Py.GIL())
            {
                device.play_uri(url,meta);
            }
        }
        public void Play(dynamic device)
        {
            using (Py.GIL())
            {
                device.play();
            }
        }
        public void Stop(dynamic device) {
            using (Py.GIL())
            {
                device.stop();
            }
        }
        public void Pause(dynamic device)
        {
            using (Py.GIL()) 
            {
                device.pause();
            }
        }
        public void Seek(dynamic device, string seektime) 
        {
            using (Py.GIL())
            {
                device.seek(seektime);
            }
        }
    }
}
