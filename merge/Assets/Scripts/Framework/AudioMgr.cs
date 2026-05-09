using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioMgr : MonoSingle<AudioMgr>
{
    private readonly List<AudioSource> m_FreeSources = new List<AudioSource>();
    private readonly List<AudioSource> m_UsedSources = new List<AudioSource>();

    private AssetsLoader m_AssetsLoader;
    private Transform m_AudioRoot;
    private AudioSource m_BgmSource;
    private string m_CurrentBgmAddress;

    protected override void OnInit()
    {
        base.OnInit();
        m_AssetsLoader = gameObject.GetOrAddComponent<AssetsLoader>();
        CreateAudioRoot();
        m_BgmSource = CreateAudioSource("BGMSource", true);
    }

    public void PlayBgm(AudioClip clip, bool loop = true, float volume = 1f)
    {
        if (clip == null)
        {
            Log.Warning("AudioMgr.PlayBgm ignored: clip is null.");
            return;
        }

        m_BgmSource.clip = clip;
        m_BgmSource.loop = loop;
        m_BgmSource.volume = volume;
        m_BgmSource.Play();
    }

    public void PlayBgm(string address, bool loop = true, float volume = 1f)
    {
        if (string.IsNullOrEmpty(address))
        {
            Log.Warning("AudioMgr.PlayBgm ignored: address is null or empty.");
            return;
        }

        m_AssetsLoader.LoadAssetAsync<AudioClip>(address, clip =>
        {
            if (clip == null)
            {
                Log.Error($"AudioMgr.PlayBgm failed: clip load failed. address={address}");
                return;
            }

            ReleaseBgmAddress();
            m_CurrentBgmAddress = address;
            PlayBgm(clip, loop, volume);
        });
    }

    public void StopBgm()
    {
        if (m_BgmSource == null)
        {
            return;
        }

        m_BgmSource.Stop();
        m_BgmSource.clip = null;
        ReleaseBgmAddress();
    }

    public void PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (clip == null)
        {
            Log.Warning("AudioMgr.PlaySfx ignored: clip is null.");
            return;
        }

        AudioSource audioSource = GetAudioSource();
        audioSource.clip = clip;
        audioSource.loop = false;
        audioSource.volume = volume;
        audioSource.Play();
        StartCoroutine(RecoverAudioSource(audioSource));
    }

    public void PlaySfx(string address, float volume = 1f)
    {
        if (string.IsNullOrEmpty(address))
        {
            Log.Warning("AudioMgr.PlaySfx ignored: address is null or empty.");
            return;
        }

        m_AssetsLoader.LoadAssetAsync<AudioClip>(address, clip =>
        {
            if (clip == null)
            {
                Log.Error($"AudioMgr.PlaySfx failed: clip load failed. address={address}");
                return;
            }

            PlaySfxInternal(clip, volume, address);
        });
    }

    public void StopAllSfx()
    {
        for (int i = m_UsedSources.Count - 1; i >= 0; i--)
        {
            ReleaseAudioSource(m_UsedSources[i]);
        }
    }

    private void CreateAudioRoot()
    {
        Transform existedRoot = transform.Find("AudioRoot");
        if (existedRoot != null)
        {
            m_AudioRoot = existedRoot;
            return;
        }

        m_AudioRoot = new GameObject("AudioRoot").transform;
        m_AudioRoot.SetParent(transform, false);
    }

    private AudioSource GetAudioSource()
    {
        AudioSource audioSource;
        if (m_FreeSources.Count > 0)
        {
            int lastIndex = m_FreeSources.Count - 1;
            audioSource = m_FreeSources[lastIndex];
            m_FreeSources.RemoveAt(lastIndex);
        }
        else
        {
            audioSource = CreateAudioSource($"SFXSource_{m_UsedSources.Count}", false);
        }

        audioSource.gameObject.SetActive(true);
        m_UsedSources.Add(audioSource);
        return audioSource;
    }

    private void PlaySfxInternal(AudioClip clip, float volume, string address)
    {
        AudioSource audioSource = GetAudioSource();
        audioSource.clip = clip;
        audioSource.loop = false;
        audioSource.volume = volume;
        audioSource.Play();
        StartCoroutine(RecoverAudioSource(audioSource, address));
    }

    private AudioSource CreateAudioSource(string nodeName, bool loop)
    {
        GameObject node = new GameObject(nodeName);
        node.transform.SetParent(m_AudioRoot, false);
        AudioSource audioSource = node.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = loop;
        return audioSource;
    }

    private IEnumerator RecoverAudioSource(AudioSource audioSource, string address = null)
    {
        yield return new WaitUntil(() => audioSource == null || !audioSource.isPlaying);
        if (!string.IsNullOrEmpty(address))
        {
            m_AssetsLoader.Release(address);
        }

        ReleaseAudioSource(audioSource);
    }

    private void ReleaseAudioSource(AudioSource audioSource)
    {
        if (audioSource == null)
        {
            return;
        }

        audioSource.Stop();
        audioSource.clip = null;
        audioSource.gameObject.SetActive(false);
        m_UsedSources.Remove(audioSource);
        if (!m_FreeSources.Contains(audioSource))
        {
            m_FreeSources.Add(audioSource);
        }
    }

    private void ReleaseBgmAddress()
    {
        if (string.IsNullOrEmpty(m_CurrentBgmAddress))
        {
            return;
        }

        m_AssetsLoader.Release(m_CurrentBgmAddress);
        m_CurrentBgmAddress = null;
    }
}
