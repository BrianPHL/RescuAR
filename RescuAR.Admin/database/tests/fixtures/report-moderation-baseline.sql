-- Report table shape, grants and policies from the supplied 2026-10-07 report.
-- Apply after step1-baseline.sql and migration 001. Fixture rows are synthetic.
CREATE TABLE public.community_reports (
    id text PRIMARY KEY, title text NOT NULL, description text, address text,
    latitude double precision DEFAULT 14.6585, longitude double precision DEFAULT 121.0955,
    posted_by text DEFAULT 'User', category text DEFAULT 'Flood Warning',
    severity text DEFAULT 'Medium', status text DEFAULT 'Pending',
    created_at timestamptz DEFAULT now(), media_url text,
    like_count integer DEFAULT 0, comments_json text DEFAULT '[]'
);
ALTER TABLE public.community_reports ENABLE ROW LEVEL SECURITY;
CREATE POLICY "Allow public insert access" ON public.community_reports FOR INSERT WITH CHECK (true);
CREATE POLICY "Allow public read access" ON public.community_reports FOR SELECT USING (true);
CREATE POLICY "Allow public update access" ON public.community_reports FOR UPDATE USING (true);
