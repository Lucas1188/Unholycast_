using Python.Runtime;

namespace Unholycast_
{
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
                    return soco.SoCo(nameOrIp);

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
