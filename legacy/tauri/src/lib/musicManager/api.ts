/**
 * Music Manager API - typed functions for interacting with the NCBF Music Manager
 * Supabase tables (songs, sets, groups, etc.)
 */

import { supabase } from '../supabase';
import type {
  MusicGroup,
  Song,
  SongArrangement,
  SongAsset,
  Set,
  SetSong,
  SongWithAssets,
  SetWithSongs,
  PresenterLink,
  SyncRequest,
} from '../supabase';

const musicDb = supabase.schema('music');

// ============================================================================
// Music Groups
// ============================================================================

export async function fetchMusicGroups(): Promise<MusicGroup[]> {
  const { data, error } = await musicDb
    .from('music_groups')
    .select('*')
    .order('name');

  if (error) throw new Error(`Failed to fetch music groups: ${error.message}`);
  return data ?? [];
}

export async function fetchMusicGroup(id: string): Promise<MusicGroup | null> {
  const { data, error } = await musicDb
    .from('music_groups')
    .select('*')
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch music group: ${error.message}`);
  }
  return data;
}

// ============================================================================
// Songs
// ============================================================================

export async function fetchSongs(groupId?: string): Promise<Song[]> {
  let query = musicDb.from('songs').select('*').order('title');

  if (groupId) {
    query = query.eq('group_id', groupId);
  }

  const { data, error } = await query;

  if (error) throw new Error(`Failed to fetch songs: ${error.message}`);
  return data ?? [];
}

export async function fetchSong(id: string): Promise<Song | null> {
  const { data, error } = await musicDb
    .from('songs')
    .select('*')
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch song: ${error.message}`);
  }
  return data;
}

export async function fetchSongWithAssets(id: string): Promise<SongWithAssets | null> {
  const { data, error } = await musicDb
    .from('songs')
    .select(`
      *,
      song_assets (*),
      song_arrangements (*)
    `)
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch song with assets: ${error.message}`);
  }
  return data as SongWithAssets | null;
}

export async function searchSongs(query: string, groupId?: string): Promise<Song[]> {
  let dbQuery = musicDb
    .from('songs')
    .select('*')
    .ilike('title', `%${query}%`)
    .order('title')
    .limit(50);

  if (groupId) {
    dbQuery = dbQuery.eq('group_id', groupId);
  }

  const { data, error } = await dbQuery;

  if (error) throw new Error(`Failed to search songs: ${error.message}`);
  return data ?? [];
}

// ============================================================================
// Song Assets (for lyrics extraction)
// ============================================================================

export async function fetchSongAssets(songId: string): Promise<SongAsset[]> {
  const { data, error } = await musicDb
    .from('song_assets')
    .select('*')
    .eq('song_id', songId)
    .order('created_at', { ascending: false });

  if (error) throw new Error(`Failed to fetch song assets: ${error.message}`);
  return data ?? [];
}

export type ArrangementSlide = {
  label?: string | null;
  lines?: string[] | null;
};

type SlideGroupRow = {
  label?: string | null;
  custom_label?: string | null;
  slides?: unknown;
};

type SlideEntry = {
  position?: number | null;
  lines?: unknown;
};

function buildSlideGroupLabel(label?: string | null, customLabel?: string | null): string | null {
  const base = typeof label === 'string' ? label.trim() : '';
  const suffix = typeof customLabel === 'string' ? customLabel.trim() : '';
  if (!base && !suffix) return null;
  if (base && suffix) return `${base} ${suffix}`;
  return base || suffix || null;
}

function normalizeSlideGroupSlides(
  slides: unknown,
  label: string | null
): ArrangementSlide[] {
  if (!Array.isArray(slides)) return [];

  return slides
    .filter((slide) => slide && typeof slide === 'object')
    .map((slide) => {
      const entry = slide as SlideEntry;
      const lines = Array.isArray(entry.lines)
        ? entry.lines.filter((line) => typeof line === 'string') as string[]
        : [];
      return {
        position: typeof entry.position === 'number' ? entry.position : null,
        lines,
      };
    })
    .filter((entry) => entry.lines.length > 0)
    .sort((a, b) => (a.position ?? 0) - (b.position ?? 0))
    .map((entry) => ({ label, lines: entry.lines }));
}

export async function fetchSongArrangementSlides(songId: string): Promise<ArrangementSlide[] | null> {
  const { data: arrangements, error: arrangementsError } = await musicDb
    .from('song_arrangements')
    .select('id, name, created_at')
    .eq('song_id', songId)
    .order('created_at', { ascending: true });

  if (arrangementsError) {
    throw new Error(`Failed to fetch song arrangements: ${arrangementsError.message}`);
  }

  const preferred =
    arrangements?.find((arr) => arr.name?.toLowerCase() === 'default') ??
    arrangements?.[0];

  if (!preferred) return null;

  const { data: arrangementGroups, error: groupError } = await musicDb
    .from('song_arrangement_groups')
    .select('position, song_slide_groups (label, custom_label, slides)')
    .eq('arrangement_id', preferred.id)
    .order('position', { ascending: true });

  if (groupError) {
    throw new Error(`Failed to fetch song arrangement groups: ${groupError.message}`);
  }

  const groupedSlides: ArrangementSlide[] = [];
  for (const group of arrangementGroups ?? []) {
    const slideGroup = Array.isArray(group.song_slide_groups)
      ? group.song_slide_groups[0]
      : group.song_slide_groups;
    if (!slideGroup) continue;

    const label = buildSlideGroupLabel(slideGroup.label, slideGroup.custom_label);
    groupedSlides.push(...normalizeSlideGroupSlides(slideGroup.slides, label));
  }

  if (groupedSlides.length > 0) return groupedSlides;

  const { data: slideGroups, error: slideGroupError } = await musicDb
    .from('song_slide_groups')
    .select('label, custom_label, slides')
    .eq('song_id', songId)
    .order('position', { ascending: true });

  if (slideGroupError) {
    throw new Error(`Failed to fetch song slide groups: ${slideGroupError.message}`);
  }

  const fallbackSlides = (slideGroups ?? []).flatMap((group) => {
    const label = buildSlideGroupLabel(group.label, group.custom_label);
    return normalizeSlideGroupSlides(group.slides, label);
  });

  return fallbackSlides.length > 0 ? fallbackSlides : null;
}

// ============================================================================
// Song Arrangements
// ============================================================================

export async function fetchSongArrangements(songId: string): Promise<SongArrangement[]> {
  const { data, error } = await musicDb
    .from('song_arrangements')
    .select('*')
    .eq('song_id', songId)
    .order('name');

  if (error) throw new Error(`Failed to fetch arrangements: ${error.message}`);
  return data ?? [];
}

// ============================================================================
// Sets (Set Lists)
// ============================================================================

export async function fetchSets(groupId: string): Promise<Set[]> {
  const { data, error } = await musicDb
    .from('sets')
    .select('*')
    .eq('group_id', groupId)
    .order('service_date', { ascending: false });

  if (error) throw new Error(`Failed to fetch sets: ${error.message}`);
  return data ?? [];
}

export async function fetchSet(id: string): Promise<Set | null> {
  const { data, error } = await musicDb
    .from('sets')
    .select('*')
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch set: ${error.message}`);
  }
  return data;
}

export async function fetchSetWithSongs(id: string): Promise<SetWithSongs | null> {
  const { data, error } = await musicDb
    .from('sets')
    .select(`
      *,
      set_songs (
        *,
        songs (*)
      )
    `)
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch set with songs: ${error.message}`);
  }

  // Sort set_songs by position
  if (data) {
    const typedData = data as SetWithSongs;
    if (typedData.set_songs) {
      typedData.set_songs.sort((a, b) => a.position - b.position);
    }
    return typedData;
  }

  return null;
}

export async function fetchSetSongs(setId: string): Promise<(SetSong & { songs?: Song })[]> {
  const { data, error } = await musicDb
    .from('set_songs')
    .select(`
      *,
      songs (*)
    `)
    .eq('set_id', setId)
    .order('position');

  if (error) throw new Error(`Failed to fetch set songs: ${error.message}`);
  return (data ?? []) as (SetSong & { songs?: Song })[];
}

// ============================================================================
// Presenter Links (sync status)
// ============================================================================

export async function fetchPresenterLink(localPresentationId: string): Promise<PresenterLink | null> {
  const { data, error } = await musicDb
    .from('presenter_links')
    .select('*')
    .eq('local_presentation_id', localPresentationId)
    .maybeSingle();

  if (error) throw new Error(`Failed to fetch presenter link: ${error.message}`);
  return data;
}

export async function upsertPresenterLink(
  link: Omit<PresenterLink, 'id' | 'updated_at'>
): Promise<PresenterLink> {
  const insertData = {
    ...link,
    updated_at: new Date().toISOString(),
  };

  const { data, error } = await musicDb
    .from('presenter_links')
    .upsert(insertData, {
      onConflict: 'local_presentation_id',
    })
    .select()
    .single();

  if (error) throw new Error(`Failed to upsert presenter link: ${error.message}`);
  return data;
}

// ============================================================================
// Sync Requests
// ============================================================================

export async function createSyncRequest(
  request: Omit<SyncRequest, 'id' | 'status' | 'conflict_url' | 'created_at' | 'updated_at'>
): Promise<SyncRequest> {
  const insertData = {
    ...request,
    status: 'queued' as const,
  };

  const { data, error } = await musicDb
    .from('sync_requests')
    .insert(insertData)
    .select()
    .single();

  if (error) throw new Error(`Failed to create sync request: ${error.message}`);
  return data;
}

export async function fetchSyncRequest(id: string): Promise<SyncRequest | null> {
  const { data, error } = await musicDb
    .from('sync_requests')
    .select('*')
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch sync request: ${error.message}`);
  }
  return data;
}

export async function fetchPendingSyncRequests(groupId: string): Promise<SyncRequest[]> {
  const { data, error } = await musicDb
    .from('sync_requests')
    .select('*')
    .eq('group_id', groupId)
    .in('status', ['queued', 'conflict'])
    .order('created_at', { ascending: false });

  if (error) throw new Error(`Failed to fetch pending sync requests: ${error.message}`);
  return data ?? [];
}

// ============================================================================
// Convenience: Create set update sync request (for playlist reorder)
// ============================================================================

export async function requestSetUpdate(
  groupId: string,
  setId: string,
  newOrder: { songId: string; position: number }[]
): Promise<SyncRequest> {
  return createSyncRequest({
    group_id: groupId,
    type: 'set_update',
    target_id: setId,
    base_version: null, // Could track version if we implement optimistic locking
    payload: {
      order: newOrder,
    },
  });
}
