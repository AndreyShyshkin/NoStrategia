import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "NoStrategia Telemetry | 60 FPS Live Analytics",
  description: "Assetto Corsa Competizione High-Performance Telemetry Dashboard, 60 FPS Canvas and Audio Race Engineer",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="uk" className="dark">
      <body className="min-h-screen bg-[#0a0c10] text-gray-100 antialiased selection:bg-cyan-500 selection:text-black">
        {children}
      </body>
    </html>
  );
}
