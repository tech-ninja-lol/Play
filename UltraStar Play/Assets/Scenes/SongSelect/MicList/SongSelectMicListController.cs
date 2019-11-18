﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SongSelectMicListController : MonoBehaviour
{
    public SongSelectMicListEntry listEntryPrefab;
    public GameObject scrollViewContent;

    void Start()
    {
        UpdateListEntries();
    }

    private void UpdateListEntries()
    {
        // Remove old entries
        foreach (Transform child in scrollViewContent.transform)
        {
            Destroy(child.gameObject);
        }

        // Create new entries
        List<MicProfile> micProfiles = SettingsManager.Instance.Settings.MicProfiles;
        List<MicProfile> enabledAndConnectedMicProfiles = micProfiles.Where(it => it.IsEnabled && it.IsConnected).ToList();
        foreach (MicProfile micProfile in enabledAndConnectedMicProfiles)
        {
            CreateListEntry(micProfile);
        }
    }

    private void CreateListEntry(MicProfile micProfile)
    {
        SongSelectMicListEntry listEntry = Instantiate(listEntryPrefab);
        listEntry.transform.SetParent(scrollViewContent.transform);
        listEntry.Init(micProfile);
    }
}