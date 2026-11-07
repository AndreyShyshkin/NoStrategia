/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: false, // React StrictMode causes double mount/unmount in dev which can reset WebSocket/Canvas buffers
};

export default nextConfig;
