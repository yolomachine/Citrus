#if OPENAL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if ANDROID
using System.Runtime.InteropServices;
#endif
using System.Threading;

#if iOS
using Foundation;
using AVFoundation;
using Lime.OpenALSoft;
#else
using OpenTK;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
#endif

namespace Lime
{
	internal static class PlatformAudioSystem
	{
#if ANDROID
		const string Lib = "openal32";
		const CallingConvention Style = CallingConvention.Cdecl;

		[DllImport(Lib, EntryPoint = "alcDevicePauseSOFT", ExactSpelling = true, CallingConvention = Style)]
		unsafe static extern void AlcDevicePauseSoft(IntPtr device);

		[DllImport(Lib, EntryPoint = "alcDeviceResumeSOFT", ExactSpelling = true, CallingConvention = Style)]
		unsafe static extern void AlcDeviceResumeSoft(IntPtr device);
#endif

		private static readonly List<AudioChannel> channels = new List<AudioChannel>();
		private static readonly List<AudioChannel> exclusiveChannels = new List<AudioChannel>();
		public static readonly float[] exclusiveVolumes = { 1, 1, 1 };
		private static AudioContext context;
		private static Thread streamingThread = null;
		private static volatile bool shouldTerminateThread;
		private static readonly AudioCache cache = new AudioCache();
#if iOS
		static NSObject interruptionNotification;
		static bool audioSessionInterruptionEnded;
#endif

		public static event Action<string> AudioMissing;

		public static void Initialize(ApplicationOptions options)
		{
#if iOS
			AVAudioSession.SharedInstance().Init();
			interruptionNotification = AVAudioSession.Notifications.ObserveInterruption((sender, args) => {
				if (args.InterruptionType == AVAudioSessionInterruptionType.Began) {
					AVAudioSession.SharedInstance().SetActive(false);
					// OpenALMob can not continue after session interruption, so destroy context here.
					if (context != null) {
						foreach (var c in channels) {
							c.DisposeOpenALResources();
						}
						context.Dispose();
						context = null;
					}
					Active = false;
				} else if (args.InterruptionType == AVAudioSessionInterruptionType.Ended) {
					// Do not restore the audio session here, because incoming call screen is still visible. Defer it until the first update.
					audioSessionInterruptionEnded = true;
				}
			});
			context = new AudioContext();
#elif ANDROID
			// LoadLibrary() ivokes JNI_OnLoad()
			Java.Lang.JavaSystem.LoadLibrary(Lib);

			// Some devices can throw AudioContextException : The audio context could not be created with the specified parameters
			// while AudioContext initializing. Try initialize context multiple times to avoid it.
			for (int i = 0; i < 3; i++) {
				try {
					context = new AudioContext();
					Logger.Write($"AudioContext initialized successfully");
					break;
				} catch (System.Exception e) {
					Logger.Write($"Initialize AudioContext error: {e.Message}");
				}
			}
#else
			bool isDeviceAvailable = !String.IsNullOrEmpty(AudioContext.DefaultDevice);
			if (isDeviceAvailable && !CommandLineArgs.NoAudio) {
				context = new AudioContext();
			}
#endif
			var err = AL.GetError();
			if (err == ALError.NoError) {
				for (int i = 0; i < options.NumChannels; i++) {
					channels.Add(new AudioChannel(i));
				}
			}
			if (options.DecodeAudioInSeparateThread) {
				streamingThread = new Thread(RunStreamingLoop);
				streamingThread.IsBackground = true;
				streamingThread.Start();
			}
		}

		public static bool Active
		{
#if iOS
			get { return context != null && Alc.GetCurrentContext() != IntPtr.Zero; }
#else
			get { return context != null && Alc.GetCurrentContext().Handle != IntPtr.Zero; }
#endif
			set
			{
				if (Active != value) {
					SetActive(value);
				}
			}
		}

		public static IEnumerable<IAudioChannel> Channels => channels;

#if ANDROID
		private static void SetActive(bool value)
		{
			if (value) {
				if (context != null) {
					try {
						context.MakeCurrent();
					} catch (AudioContextException) {
						Logger.Write("Error: failed to resume OpenAL after interruption ended");
					}
				}
				AlcDeviceResumeSoft(Alc.GetContextsDevice(Alc.GetCurrentContext()));
			} else {
				AlcDevicePauseSoft(Alc.GetContextsDevice(Alc.GetCurrentContext()));
				Alc.MakeContextCurrent(ContextHandle.Zero);
			}
		}
#elif iOS
		private static void SetActive(bool value)
		{
			if (value) {
				context?.MakeCurrent();
			} else {
				Alc.MakeContextCurrent(IntPtr.Zero);
			}
		}
#else
		private static void SetActive(bool value)
		{
			if (value) {
				if (context != null) {
					try {
						context.MakeCurrent();
					} catch (AudioContextException) {
						Logger.Write("Error: failed to resume OpenAL after interruption ended");
					}
				}
				ResumeAll();
			} else {
				PauseAll();
				Alc.MakeContextCurrent(ContextHandle.Zero);
			}
		}
#endif

		public static void Terminate()
		{
			if (streamingThread != null) {
				shouldTerminateThread = true;
				streamingThread.Join();
				streamingThread = null;
			}
			foreach (var channel in channels) {
				channel.Dispose();
			}
			channels.Clear();
			if (context != null) {
				context.Dispose();
				context = null;
			}
#if iOS
			if (interruptionNotification != null) {
				interruptionNotification.Dispose();
				interruptionNotification = null;
			}
#endif
		}

		private static long tickCount;

		private static long GetTimeDelta()
		{
			long delta = (DateTime.Now.Ticks / 10000L) - tickCount;
			if (tickCount == 0) {
				tickCount = delta;
				delta = 0;
			} else {
				tickCount += delta;
			}
			return delta;
		}

		private static AudioChannel FindTopExclusiveChannel(AudioChannelGroup group)
		{
			for (int i = exclusiveChannels.Count - 1; i >= 0; i--) {
				if (exclusiveChannels[i].Group == group) {
					return exclusiveChannels[i];
				}
			}
			return null;
		}

		internal static void CheckExclusiveStop(AudioChannel audioChannel)
		{
			var group = audioChannel.Group;
			AudioChannel topChannel = FindTopExclusiveChannel(group);
			if (topChannel == null) {
				return;
			}
			exclusiveChannels.Remove(audioChannel);
			// ??? not stopped ???
			if (topChannel == audioChannel) {
				var nextExclusiveChannel = FindTopExclusiveChannel(group);
				if (nextExclusiveChannel == null) {
					exclusiveVolumes[(int)group] = 1.0f;
				}
				foreach (var channel in channels) {
					channel.Volume = channel.Volume;
				}
				//}
			}
		}

		private static void RunStreamingLoop()
		{
			while (!shouldTerminateThread) {
				UpdateChannels();
				Thread.Sleep(10);
			}
		}

		public static void Update()
		{
#if iOS
			if (audioSessionInterruptionEnded) {
				audioSessionInterruptionEnded = false;
				AVAudioSession.SharedInstance().SetActive(true);
				context = new AudioContext();
				foreach (var c in channels) {
					c.CreateOpenALResources();
				}
			}
#endif
			if (streamingThread == null) {
				UpdateChannels();
			}
		}

		private static void UpdateChannels()
		{
			float delta = GetTimeDelta() * 0.001f;
			foreach (var channel in channels) {
				channel.Volume = channel.Volume;
				channel.Update(delta);
			}
		}

		public static void SetGroupVolume(AudioChannelGroup group, float value)
		{
			foreach (var channel in channels) {
				if (channel.Group == group) {
					channel.Volume = channel.Volume;
				}
			}
		}

		public static void PauseGroup(AudioChannelGroup group)
		{
			foreach (var channel in channels) {
				if (channel.Group == group && channel.State == AudioChannelState.Playing) {
					channel.Pause();
				}
			}
		}

		public static void ResumeGroup(AudioChannelGroup group)
		{
			foreach (var channel in channels) {
				if (channel.Group == group && channel.State == AudioChannelState.Paused) {
					channel.Resume();
				}
			}
		}

		internal static float GetExclusiveVolume(AudioChannel audioChannel) =>
			FindTopExclusiveChannel(audioChannel.Group) == audioChannel
				? 1.0f
				: exclusiveVolumes[(int)audioChannel.Group];

		public static void PauseAll()
		{
			foreach (var channel in channels) {
				if (channel.State == AudioChannelState.Playing) {
					channel.Pause();
				}
			}
		}

		public static void ResumeAll()
		{
			foreach (var channel in channels) {
				if (channel.State == AudioChannelState.Paused) {
					channel.Resume();
				}
			}
		}

		public static void StopAll()
		{
			foreach (var channel in channels) {
				channel.Stop();
			}
		}

		public static void StopGroup(AudioChannelGroup group, float fadeoutTime)
		{
			foreach (var channel in channels) {
				if (channel.Group == group) {
					channel.Stop(fadeoutTime);
				}
			}
		}

		private static Sound LoadSoundToChannel(AudioChannel channel, PlayParameters parameters, float fadeinTime)
		{
			if (context == null) {
				return new Sound();
			}
			var path = parameters.Path;
			var sound = new Sound();
			var decoder = parameters.Decoder;
			if (decoder == null) {
#if TANGERINE
				path += ".ogg";
#else
				path += ".sound";
#endif // TANGERINE
				var stream = cache.OpenStream(path);
				if (stream == null) {
					AudioMissing?.Invoke(path);
					return sound;
				}
				decoder = AudioDecoderFactory.CreateDecoder(stream);
			}
			if (channel == null || !channel.Play(sound, decoder, parameters.Looping, parameters.Paused, fadeinTime)) {
				decoder.Dispose();
				return sound;
			}
			channel.SamplePath = path;
			if (parameters.Exclusive) {
				if (exclusiveChannels.Contains(channel)) {
					exclusiveChannels.Remove(channel);
				}
				exclusiveVolumes[(int)channel.Group] = 0.0f;
				exclusiveChannels.Add(channel);
				foreach (var c in channels) {
					if (c.Group == channel.Group) {
						c.Volume = c.Volume;
					}
				}
			}
			return sound;
		}

		private static AudioChannel AllocateChannel(float priority)
		{
			channels.Sort((a, b) => {
				if (a.Priority != b.Priority) {
					return Mathf.Sign(a.Priority - b.Priority);
				}
				if (a.StartupTime == b.StartupTime) {
					return a.Id - b.Id;
				}
				return (a.StartupTime < b.StartupTime) ? -1 : 1;
			});
			// Looking for stopped channels
			foreach (var channel in channels) {
				if (channel.Streaming) {
					continue;
				}
				var state = channel.State;
				if (state == AudioChannelState.Stopped || state == AudioChannelState.Initial) {
					return channel;
				}
			}
			// Trying to stop first channel in order of priority
			foreach (var channel in channels) {
				if (channel.Priority <= priority) {
					channel.Stop();
					if (channel.State == AudioChannelState.Stopped) {
						return channel;
					}
				}
			}
			return null;
		}

		public static Sound Play(PlayParameters parameters, float fadeinTime = 0f)
		{
			var channel = AllocateChannel(parameters.Priority);
			if (channel == null) {
				return new Sound();
			}
			if (channel.Sound != null) {
				channel.Sound.ChannelInternal = NullAudioChannel.Instance;
			}
			channel.Group = parameters.Group;
			channel.Priority = parameters.Priority;
			channel.Volume = parameters.Volume;
			channel.Pitch = parameters.Pitch;
			channel.Pan = parameters.Pan;
			return LoadSoundToChannel(channel, parameters, fadeinTime);
		}

		public static Sound Play(
			Stream stream,
			AudioChannelGroup group,
			float priority = 0.5f,
			float fadeinTime = 0f,
			bool paused = false,
			float volume = 1f,
			float pan = 0f,
			float pitch = 1f
		)
		{
			var channel = GetAudioChannel(group, priority, volume, pan, pitch);
			if (channel == null) {
				return new Sound();
			}
			if (context == null) {
				return new Sound();
			}
			var sound = new Sound();
			var decoder = new PcmDecoder(stream);
			if (channel == null || !channel.Play(sound, decoder, false, paused, fadeinTime)) {
				decoder.Dispose();
				return sound;
			}
			channel.SamplePath = "";
			return sound;
		}

		private static AudioChannel GetAudioChannel(
			AudioChannelGroup group,
			float priority = 0.5f,
			float volume = 1f,
			float pan = 0f,
			float pitch = 1f
		)
		{
			var channel = AllocateChannel(priority);
			if (channel == null) {
				return null;
			}
			if (channel.Sound != null) {
				channel.Sound.ChannelInternal = NullAudioChannel.Instance;
			}
			channel.Group = group;
			channel.Priority = priority;
			channel.Volume = volume;
			channel.Pitch = pitch;
			channel.Pan = pan;
			return channel;
		}

		public struct ErrorChecker : IDisposable
		{
			private string comment;
			private bool throwException;

			public ErrorChecker(string comment = null, bool throwException = true)
			{
				this.comment = comment;
				this.throwException = throwException;
				// Clear current error
				AL.GetError();
			}

			void IDisposable.Dispose()
			{
				var error = AL.GetError();
				if (error != ALError.NoError) {
					string message = "OpenAL error: " + AL.GetErrorString(error);
					if (comment != null) {
						message += string.Format(" ({0})", comment);
					}
					if (throwException) {
						throw new Exception(message);
					} else {
						Logger.Write(message);
					}
				}
			}
		}
	}

#if iOS
	class AudioContext : IDisposable
	{
		IntPtr handle;

		public unsafe AudioContext()
		{
			var device = Alc.OpenDevice(null);
			handle = Alc.CreateContext(device, (int*)null);
			MakeCurrent();
		}

		public void MakeCurrent()
		{
			Alc.MakeContextCurrent(handle);
		}

		public void Suspend()
		{
			Alc.SuspendContext(handle);
		}

		public void Process()
		{
			Alc.ProcessContext(handle);
		}

		public void Dispose()
		{
			if (handle != IntPtr.Zero) {
				handle = IntPtr.Zero;
				Alc.DestroyContext(handle);
			}
		}
	}
#endif
}
#endif
