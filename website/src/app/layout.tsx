import type { Metadata } from 'next';
import { GoogleAnalytics } from '@next/third-parties/google';
import { Header } from '@/components/Header';
import { Footer } from '@/components/Footer';
import { WhatsNewBanner } from '@/components/landing/WhatsNewBanner';
import './globals.css';
import { themeScript } from '@/lib/theme';
import { canonicalUrl, publicUrl, siteOrigin } from '@/lib/site';
import { getBasePath } from '@/lib/utils';
import { fontVariables } from './fonts';

export const metadata: Metadata = {
  metadataBase: new URL(siteOrigin),
  alternates: { canonical: canonicalUrl('/') },
  title: {
    default: 'Calor - Make AI-written .NET code easier to check',
    template: '%s | Calor',
  },
  description:
    'Make AI-written .NET code easier to check and review. Calor records important rules, checks supported cases, and creates regular C# code.',
  keywords: ['Calor', 'programming language', 'AI', 'coding agents', 'compiler', '.NET', 'C#'],
  authors: [{ name: 'Calor Team' }],
  icons: {
    icon: `${getBasePath()}/favicon.ico`,
  },
  openGraph: {
    type: 'website',
    locale: 'en_US',
    url: canonicalUrl('/'),
    siteName: 'Calor',
    title: 'Calor - Make AI-written .NET code easier to check',
    description:
      'Calor records important rules for AI-written code, checks supported cases, and creates regular C# code for .NET.',
    images: [
      {
        url: publicUrl('/og-image.jpg'),
        width: 1200,
        height: 630,
        alt: 'Calor - Make AI-written .NET code easier to check',
      },
    ],
  },
  twitter: {
    card: 'summary_large_image',
    title: 'Calor - Make AI-written .NET code easier to check',
    description:
      'Calor records important rules for AI-written code, checks supported cases, and creates regular C# code for .NET.',
    images: [publicUrl('/og-image.jpg')],
  },
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head><script dangerouslySetInnerHTML={{ __html: themeScript }} /></head>
      <body className="font-body antialiased" style={fontVariables}>
        <div className="relative flex min-h-screen flex-col">
          <WhatsNewBanner />
          <Header />
          <main className="flex-1">{children}</main>
          <Footer />
        </div>
      </body>
      <GoogleAnalytics gaId="G-GR98MMDPX3" />
    </html>
  );
}
