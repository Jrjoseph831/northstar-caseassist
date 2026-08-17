import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

const geistSans = Geist({ variable: "--font-geist-sans", subsets: ["latin"] });
const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Northstar CaseAssist",
  description:
    "A secure, human-centered AI casework assistant for a fictional public-services organization.",
  openGraph: {
    title: "Northstar CaseAssist",
    description: "Secure AI assistance. Human decisions.",
    images: ["/og.png"],
  },
  twitter: {
    card: "summary_large_image",
    title: "Northstar CaseAssist",
    description: "Secure AI assistance. Human decisions.",
    images: ["/og.png"],
  },
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body className={`${geistSans.variable} ${geistMono.variable}`}>
        {children}
      </body>
    </html>
  );
}
