-- 04_seed.sql: Seed Reference Data for Spa-Francorchamps and Default Benchmark Session

-- 1. Insert Default Benchmark Session
INSERT INTO sessions (
    session_id,
    session_uid,
    car_model,
    track_name,
    driver_name,
    session_type,
    ambient_temp,
    track_temp,
    started_at
) VALUES (
    '4a73b2c1-840e-436f-bca8-e9f3b259d640',
    'spa_gt3_benchmark_session',
    'mercedes_amg_gt3_evo',
    'spa',
    'Shyshkin Andrey',
    'Qualify',
    21.5,
    29.0,
    NOW() - INTERVAL '30 minutes'
) ON CONFLICT (session_id) DO NOTHING;

-- 2. Insert Reference Micro-Sectors for Circuit de Spa-Francorchamps (30-50 functional zones)
INSERT INTO micro_sectors (track_name, sector_index, sector_name, start_distance, end_distance, sector_type) VALUES
('spa', 1,  'Main Straight',                  0.000, 0.045, 'Straight'),
('spa', 2,  'La Source (Turn 1 Braking)',     0.045, 0.065, 'HeavyBraking'),
('spa', 3,  'La Source (Apex & Exit)',        0.065, 0.090, 'Apex'),
('spa', 4,  'Downhill to Eau Rouge',          0.090, 0.145, 'Straight'),
('spa', 5,  'Eau Rouge (Turn 2 Compression)', 0.145, 0.175, 'TurnIn'),
('spa', 6,  'Raidillon (Turn 3-4 Crest)',     0.175, 0.210, 'Acceleration'),
('spa', 7,  'Kemmel Straight Part 1',         0.210, 0.280, 'Straight'),
('spa', 8,  'Kemmel Straight Part 2',         0.280, 0.350, 'Straight'),
('spa', 9,  'Les Combes (Turn 5 Braking)',    0.350, 0.375, 'HeavyBraking'),
('spa', 10, 'Les Combes (Chicane 5-6)',       0.375, 0.405, 'Apex'),
('spa', 11, 'Malmedy (Turn 7 Exit)',          0.405, 0.440, 'Acceleration'),
('spa', 12, 'Rivage / Bruxelles Entry',       0.440, 0.470, 'HeavyBraking'),
('spa', 13, 'Bruxelles Hairpin (Turn 8)',     0.470, 0.510, 'Apex'),
('spa', 14, 'Speaker''s Corner (Turn 9)',     0.510, 0.545, 'Acceleration'),
('spa', 15, 'Pouhon Entry (Turn 10 Left)',    0.545, 0.585, 'TurnIn'),
('spa', 16, 'Pouhon Double Apex (Turn 11)',   0.585, 0.630, 'Apex'),
('spa', 17, 'Pouhon Exit',                    0.630, 0.670, 'Straight'),
('spa', 18, 'Campus (Pif-Paf Turn 12-13)',    0.670, 0.715, 'HeavyBraking'),
('spa', 19, 'Paul Frère (Turn 14)',           0.715, 0.755, 'Acceleration'),
('spa', 20, 'Courbe Paul Frère Exit',         0.755, 0.800, 'Straight'),
('spa', 21, 'Blanchimont 1 (Turn 15 Flat)',   0.800, 0.850, 'Straight'),
('spa', 22, 'Blanchimont 2 (Turn 16 Kink)',   0.850, 0.900, 'TurnIn'),
('spa', 23, 'Bus Stop Chicane Braking',       0.900, 0.935, 'HeavyBraking'),
('spa', 24, 'Bus Stop Chicane (Apex 17-18)',  0.935, 0.970, 'Apex'),
('spa', 25, 'Pit Straight Acceleration',      0.970, 1.000, 'Acceleration')
ON CONFLICT (track_name, sector_index) DO NOTHING;
